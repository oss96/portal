using System.Globalization;
using Portal.Core.Connections;
using Portal.Core.Shell;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace Portal.Core.Files;

/// <summary>A server's file system: SFTP for browsing and file data, shell commands for bulk work.</summary>
public sealed class RemoteFileSystem(string displayName, SftpClient sftp, RemoteShell shell, AccountNames names) : IFileSystem
{
    /// <inheritdoc />
    public string DisplayName => displayName;

    /// <inheritdoc />
    public bool IsRemote => true;

    /// <inheritdoc />
    public string RootPath => "/";

    /// <summary>The folder the server starts a session in, usually the user's home.</summary>
    public string HomeDirectory => sftp.WorkingDirectory;

    /// <summary>Names of the server's users and groups.</summary>
    public AccountNames Accounts => names;

    /// <summary>The shell this file system runs server-side commands in.</summary>
    public RemoteShell Shell => shell;

    /// <inheritdoc />
    public string Combine(string directory, string name) => RemotePath.Combine(directory, name);

    /// <inheritdoc />
    public string? Parent(string path) => RemotePath.Parent(path);

    /// <inheritdoc />
    public string NameOf(string path) => RemotePath.NameOf(path);

    /// <inheritdoc />
    public IReadOnlyList<PathSegment> Breadcrumbs(string path)
    {
        var list = new List<PathSegment> { new("/", "/") };
        var current = "";
        foreach (var seg in RemotePath.Normalize(path).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current += "/" + seg;
            list.Add(new PathSegment(seg, current));
        }
        return list;
    }

    /// <inheritdoc />
    public string? ResolveTypedPath(string text, string current)
    {
        var t = text.Trim();
        if (t.Length == 0) return null;
        if (t == "~") return HomeDirectory;
        if (t.StartsWith("~/", StringComparison.Ordinal)) return RemotePath.Normalize(RemotePath.Combine(HomeDirectory, t[2..]));
        return RemotePath.Normalize(t.StartsWith('/') ? t : RemotePath.Combine(current, t));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FileEntry>> ListAsync(string path, CancellationToken ct)
    {
        var list = new List<FileEntry>();
        await foreach (var f in sftp.ListDirectoryAsync(path, ct).ConfigureAwait(false))
        {
            if (f.Name is "." or "..") continue;
            list.Add(await ToEntryAsync(f, ct).ConfigureAwait(false));
        }
        return list;
    }

    /// <inheritdoc />
    public async Task<FileEntry?> StatAsync(string path, CancellationToken ct)
    {
        try
        {
            var a = await sftp.GetAttributesAsync(path, ct).ConfigureAwait(false);
            var kind = a.IsDirectory ? EntryKind.Directory : a.IsRegularFile ? EntryKind.File : EntryKind.Special;
            return Describe(RemotePath.NameOf(path), RemotePath.Normalize(path), kind, a);
        }
        catch (SftpPathNotFoundException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public Task CreateDirectoryAsync(string path, CancellationToken ct) => sftp.CreateDirectoryAsync(path, ct);

    /// <inheritdoc />
    public async Task RenameAsync(string from, string to, bool overwrite, CancellationToken ct)
    {
        if (!overwrite)
        {
            await sftp.RenameFileAsync(from, to, ct).ConfigureAwait(false);
            return;
        }
        await shell.RunCheckedAsync($"mv -f -- {PosixShell.Quote(from)} {PosixShell.Quote(to)}", ct).ConfigureAwait(false);
    }

    /// <summary>Deletes entries permanently; the server has no recycle bin.</summary>
    public async Task DeleteAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        if (paths.Count == 0) return;
        if (paths.Any(p => RemotePath.Normalize(p) == "/")) throw new InvalidOperationException("Portal won’t delete the server’s root folder.");
        await shell.RunCheckedAsync("rm -rf -- " + PosixShell.Join(paths), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReadToAsync(string path, Stream destination, long offset, IProgress<long> progress, CancellationToken ct)
    {
        if (offset == 0)
        {
            await sftp.DownloadFileAsync(path, destination, new InlineProgress<DownloadFileProgressReport>(r => progress.Report((long)r.TotalBytesDownloaded)), ct).ConfigureAwait(false);
            return;
        }
        await using var source = await sftp.OpenAsync(path, FileMode.Open, FileAccess.Read, ct).ConfigureAwait(false);
        source.Seek(offset, SeekOrigin.Begin);
        await StreamCopy.CopyAsync(source, destination, progress, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task WriteFromAsync(string path, Stream source, bool append, IProgress<long> progress, CancellationToken ct)
    {
        if (!append)
        {
            await sftp.UploadFileAsync(source, path, true, new InlineProgress<UploadFileProgressReport>(r => progress.Report((long)r.TotalBytesUploaded)), ct).ConfigureAwait(false);
            return;
        }
        await using var target = await sftp.OpenAsync(path, FileMode.Append, FileAccess.Write, ct).ConfigureAwait(false);
        await StreamCopy.CopyAsync(source, target, progress, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task SetModifiedAsync(string path, DateTime modifiedUtc, CancellationToken ct) =>
        Task.Run(() => sftp.SetLastWriteTimeUtc(path, modifiedUtc), ct);

    /// <summary>Sets the permission bits of one entry.</summary>
    public Task SetModeAsync(string path, int mode, CancellationToken ct) =>
        Task.Run(() => sftp.ChangePermissions(path, (short)(mode & 0xFFF)), ct);

    /// <summary>Changes mode and/or owner of entries, optionally for everything inside folders too.</summary>
    public async Task SetPermissionsAsync(IReadOnlyList<string> paths, int? mode, string? owner, string? group, bool recursive, CancellationToken ct)
    {
        if (paths.Count == 0) return;
        var r = recursive ? "-R " : "";
        var targets = PosixShell.Join(paths);
        var steps = new List<string>();
        if (mode is int m) steps.Add($"chmod {r}{Permissions.ToOctal(m)} -- {targets}");
        if (owner is not null || group is not null)
        {
            var spec = owner is not null && group is not null ? $"{owner}:{group}" : owner ?? ":" + group;
            steps.Add($"chown {r}{PosixShell.Quote(spec)} -- {targets}");
        }
        if (steps.Count == 0) return;
        await shell.RunCheckedAsync(string.Join(" && ", steps), ct).ConfigureAwait(false);
    }

    /// <summary>Total size of a folder and everything inside it, measured on the server.</summary>
    public async Task<long> DirectorySizeAsync(string path, CancellationToken ct)
    {
        var q = PosixShell.Quote(path);
        var output = await shell.RunCheckedAsync($"if du -sb -- {q} >/dev/null 2>&1; then du -sb -- {q}; else echo k; du -sk -- {q}; fi", ct).ConfigureAwait(false);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var kilobytes = lines.Length > 1 && lines[0].Trim() == "k";
        var first = lines[^1].Split('\t', ' ')[0];
        return long.TryParse(first, out var n) ? (kilobytes ? n * 1024 : n) : 0;
    }

    /// <summary>Free and used space of the server's storage.</summary>
    public async Task<IReadOnlyList<DiskUsage>> DisksAsync(CancellationToken ct)
    {
        var r = await shell.RunAsync(DiskUsage.Command, ct).ConfigureAwait(false);
        return DiskUsage.ParseDf(r.Output);
    }

    /// <summary>Finds entries under <paramref name="root"/> whose name contains <paramref name="text"/>.</summary>
    public async Task<IReadOnlyList<FileEntry>> SearchAsync(string root, string text, int limit, CancellationToken ct)
    {
        var pattern = "*" + EscapeGlob(text) + "*";
        var cmd = $"find {PosixShell.Quote(root)} -maxdepth 8 -iname {PosixShell.Quote(pattern)} -printf '%y\\t%s\\t%T@\\t%p\\n' 2>/dev/null | head -n {limit}";
        var r = await shell.RunAsync(cmd, ct).ConfigureAwait(false);
        return ParseFind(r.Output);
    }

    /// <summary>Copies entries into a folder on the server, keeping modes and times.</summary>
    public Task CopyOnServerAsync(IReadOnlyList<string> paths, string destinationDirectory, CancellationToken ct) =>
        shell.RunCheckedAsync($"cp -a -- {PosixShell.Join(paths)} {PosixShell.Quote(destinationDirectory)}/", ct);

    /// <summary>Moves entries into a folder on the server.</summary>
    public Task MoveOnServerAsync(IReadOnlyList<string> paths, string destinationDirectory, CancellationToken ct) =>
        shell.RunCheckedAsync($"mv -- {PosixShell.Join(paths)} {PosixShell.Quote(destinationDirectory)}/", ct);

    internal static IReadOnlyList<FileEntry> ParseFind(string output)
    {
        var list = new List<FileEntry>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split('\t', 4);
            if (f.Length < 4) continue;
            var kind = f[0] switch { "d" => EntryKind.Directory, "f" => EntryKind.File, "l" => EntryKind.LinkToFile, _ => EntryKind.Special };
            long.TryParse(f[1], out var size);
            var seconds = double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 0;
            var path = f[3];
            list.Add(new FileEntry(RemotePath.NameOf(path), path, kind, size, DateTime.UnixEpoch.AddSeconds(seconds)));
        }
        return list;
    }

    internal static string EscapeGlob(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch is '*' or '?' or '[' or ']' or '\\') sb.Append('\\');
            sb.Append(ch);
        }
        return sb.ToString();
    }

    internal static int ModeOf(SftpFileAttributes a)
    {
        var m = 0;
        if (a.OwnerCanRead) m |= 0x100;
        if (a.OwnerCanWrite) m |= 0x80;
        if (a.OwnerCanExecute) m |= 0x40;
        if (a.GroupCanRead) m |= 0x20;
        if (a.GroupCanWrite) m |= 0x10;
        if (a.GroupCanExecute) m |= 0x8;
        if (a.OthersCanRead) m |= 0x4;
        if (a.OthersCanWrite) m |= 0x2;
        if (a.OthersCanExecute) m |= 0x1;
        if (a.IsUIDBitSet) m |= 0x800;
        if (a.IsGroupIDBitSet) m |= 0x400;
        if (a.IsStickyBitSet) m |= 0x200;
        return m;
    }

    private async Task<FileEntry> ToEntryAsync(ISftpFile f, CancellationToken ct)
    {
        EntryKind kind;
        if (f.IsSymbolicLink)
        {
            try
            {
                var target = await sftp.GetAttributesAsync(f.FullName, ct).ConfigureAwait(false);
                kind = target.IsDirectory ? EntryKind.LinkToDirectory : EntryKind.LinkToFile;
            }
            catch (SshException)
            {
                kind = EntryKind.LinkToFile;
            }
        }
        else kind = f.IsDirectory ? EntryKind.Directory : f.IsRegularFile ? EntryKind.File : EntryKind.Special;
        return Describe(f.Name, f.FullName, kind, f.Attributes);
    }

    private FileEntry Describe(string name, string fullPath, EntryKind kind, SftpFileAttributes a) =>
        new(name, fullPath, kind, kind == EntryKind.File ? a.Size : 0, a.LastWriteTimeUtc)
        {
            Mode = ModeOf(a),
            Owner = names.User(a.UserId),
            Group = names.Group(a.GroupId),
            IsHidden = name.StartsWith('.'),
        };

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
