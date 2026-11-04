using System.Globalization;
using Portal.Core.Connections;
using Portal.Core.Shell;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace Portal.Core.Files;

/// <summary>A server's file system: SFTP for browsing and file data, shell commands for bulk work.</summary>
public sealed class RemoteFileSystem(string displayName, SftpClient sftp, RemoteShell shell, AccountNames names) : IServerFileSystem
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
        var files = new List<ISftpFile>();
        await foreach (var f in sftp.ListDirectoryAsync(path, ct).ConfigureAwait(false))
            if (f.Name is not ("." or "..")) files.Add(f);
        using var gate = new SemaphoreSlim(16);
        var entries = await Task.WhenAll(files.Select(async f =>
        {
            if (!f.IsSymbolicLink) return ToEntry(f, null);
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return ToEntry(f, await LinkTargetIsDirectoryAsync(f.FullName, ct).ConfigureAwait(false));
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);
        return entries;
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

    /// <summary>Renames an entry. Without <paramref name="overwrite"/> an existing target is refused and a link is renamed as a link;
    /// with it, the caller has checked that <paramref name="to"/> is not a symbolic link, as the transfer engine does.</summary>
    public async Task RenameAsync(string from, string to, bool overwrite, CancellationToken ct)
    {
        if (overwrite)
        {
            await ReplaceWithSftpAsync(from, to, ct).ConfigureAwait(false);
            return;
        }
        if (shell.IsAvailable)
        {
            var exists = PosixShell.Quote($"“{RemotePath.NameOf(to)}” already exists.");
            var t = PosixShell.Quote(to);
            await shell.RunCheckedAsync($"if [ -e {t} ] || [ -L {t} ]; then echo {exists} >&2; exit 17; fi; mv -- {PosixShell.Quote(from)} {t}", ct).ConfigureAwait(false);
            return;
        }
        await RefuseLinkAsync(from, ct).ConfigureAwait(false);
        await sftp.RenameFileAsync(from, to, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> SymbolicLinksInAsync(string folder, CancellationToken ct)
    {
        var links = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var f in sftp.ListDirectoryAsync(folder, ct).ConfigureAwait(false))
            if (f.IsSymbolicLink) links.Add(f.Name);
        return links;
    }

    private async Task RefuseLinkAsync(string path, CancellationToken ct)
    {
        var parent = RemotePath.Parent(path) ?? "/";
        var name = RemotePath.NameOf(path);
        await foreach (var f in sftp.ListDirectoryAsync(parent, ct).ConfigureAwait(false))
            if (f.Name == name && f.IsSymbolicLink) throw new SymlinkRefusedException(path);
    }

    private async Task ReplaceWithSftpAsync(string from, string to, CancellationToken ct)
    {
        try
        {
            await Task.Run(() => sftp.RenameFile(from, to, isPosix: true), ct).ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            if (await StatAsync(to, ct).ConfigureAwait(false) is { IsDirectory: false }) await sftp.DeleteFileAsync(to, ct).ConfigureAwait(false);
            await sftp.RenameFileAsync(from, to, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Deletes entries permanently; the server has no recycle bin.</summary>
    public async Task DeleteAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        if (paths.Count == 0) return;
        if (paths.Any(p => RemotePath.Normalize(p) == "/")) throw new InvalidOperationException("Portal won’t delete the server’s root folder.");
        if (shell.IsAvailable)
        {
            await shell.RunCheckedAsync("rm -rf -- " + PosixShell.Join(paths), ct).ConfigureAwait(false);
            return;
        }
        var files = new List<string>();
        var folders = new List<string>();
        foreach (var group in paths.Select(RemotePath.Normalize).GroupBy(p => RemotePath.Parent(p) ?? "/"))
        {
            var listing = new Dictionary<string, ISftpFile>(StringComparer.Ordinal);
            await foreach (var f in sftp.ListDirectoryAsync(group.Key, ct).ConfigureAwait(false)) listing[f.Name] = f;
            foreach (var path in group)
            {
                if (!listing.TryGetValue(RemotePath.NameOf(path), out var self)) throw new SftpPathNotFoundException($"“{path}” doesn’t exist.");
                await CollectAsync(self, files, folders, ct).ConfigureAwait(false);
            }
        }
        foreach (var f in files) await sftp.DeleteFileAsync(f, ct).ConfigureAwait(false);
        foreach (var d in folders) await sftp.DeleteDirectoryAsync(d, ct).ConfigureAwait(false);
    }

    private async Task CollectAsync(ISftpFile entry, List<string> files, List<string> folders, CancellationToken ct)
    {
        if (entry.IsSymbolicLink) throw new SymlinkRefusedException(entry.FullName);
        if (!entry.IsDirectory)
        {
            files.Add(entry.FullName);
            return;
        }
        await foreach (var f in sftp.ListDirectoryAsync(entry.FullName, ct).ConfigureAwait(false))
            if (f.Name is not ("." or "..")) await CollectAsync(f, files, folders, ct).ConfigureAwait(false);
        folders.Add(entry.FullName);
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
        Task.Run(() => sftp.ChangePermissions(path, short.Parse(Permissions.ToOctal(mode & 0xFFF), System.Globalization.CultureInfo.InvariantCulture)), ct);

    /// <summary>Turns permission bits on and off and/or changes the owner, optionally for everything inside folders too.</summary>
    public async Task SetPermissionsAsync(IReadOnlyList<string> paths, PermissionDelta? mode, string? owner, string? group, bool recursive, CancellationToken ct)
    {
        if (paths.Count == 0) return;
        var changesMode = mode is { IsEmpty: false };
        var changesOwner = owner is not null || group is not null;
        if (!shell.IsAvailable)
        {
            if (changesOwner) throw new ShellUnavailableException();
            if (changesMode) foreach (var p in paths) await ApplyModeAsync(p, mode!, recursive, ct).ConfigureAwait(false);
            return;
        }
        var r = recursive ? "-R " : "";
        var targets = PosixShell.Join(paths);
        var steps = new List<string>();
        if (changesMode) steps.Add($"chmod {r}{PosixShell.Quote(mode!.ToChmod())} -- {targets}");
        if (changesOwner)
        {
            var spec = owner is not null && group is not null ? $"{owner}:{group}" : owner ?? ":" + group;
            steps.Add($"chown {r}{PosixShell.Quote(spec)} -- {targets}");
        }
        if (steps.Count == 0) return;
        await shell.RunCheckedAsync(string.Join(" && ", steps), ct).ConfigureAwait(false);
    }

    private async Task ApplyModeAsync(string path, PermissionDelta delta, bool recursive, CancellationToken ct)
    {
        var a = await sftp.GetAttributesAsync(path, ct).ConfigureAwait(false);
        await SetModeAsync(path, delta.ApplyTo(ModeOf(a)), ct).ConfigureAwait(false);
        if (!recursive || !a.IsDirectory) return;
        await foreach (var f in sftp.ListDirectoryAsync(path, ct).ConfigureAwait(false))
            if (f.Name is not ("." or "..") && !f.IsSymbolicLink) await ApplyModeAsync(f.FullName, delta, true, ct).ConfigureAwait(false);
    }

    private bool? _gnuFind;

    /// <summary>True when the account can run commands, so search, disk usage and server-side copy work.</summary>
    public bool CanRunCommands => shell.IsAvailable;

    /// <inheritdoc />
    public string? NoCommandsReason => shell.IsAvailable ? null : shell.IsSftpOnly
        ? $"{displayName} allows file transfers only for this account."
        : $"Portal couldn’t run commands on {displayName} for this connection. Reconnecting may help.";

    /// <summary>Total size of a folder and everything inside it, measured on the server.</summary>
    public async Task<long> DirectorySizeAsync(string path, CancellationToken ct)
    {
        if (!shell.IsAvailable) return await SumSizesAsync(path, ct).ConfigureAwait(false);
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
        if (!shell.IsAvailable) return [];
        var r = await shell.RunAsync(DiskUsage.Command, ct).ConfigureAwait(false);
        return DiskUsage.ParseDf(r.Output);
    }

    /// <summary>Finds entries under <paramref name="root"/> whose name contains <paramref name="text"/>.</summary>
    public async Task<IReadOnlyList<FileEntry>> SearchAsync(string root, string text, int limit, CancellationToken ct)
    {
        if (!shell.IsAvailable) return [];
        _gnuFind ??= (await shell.RunAsync("find / -maxdepth 0 -printf '' 2>/dev/null", ct).ConfigureAwait(false)).Succeeded;
        var find = $"find {PosixShell.Quote(root)} -maxdepth 8 -iname {PosixShell.Quote("*" + EscapeGlob(text) + "*")}";
        var cmd = _gnuFind == true
            ? $"{find} -printf '%y\\t%s\\t%T@\\t%p\\n' 2>/dev/null | head -n {limit}"
            : $"{find} 2>/dev/null | head -n {limit} | while IFS= read -r p; do stat -c '%F\t%s\t%Y\t%n' \"$p\"; done 2>/dev/null";
        var r = await shell.RunAsync(cmd, ct).ConfigureAwait(false);
        return ParseFind(r.Output);
    }

    /// <summary>Copies one entry to <paramref name="destination"/>, keeping modes and times; <paramref name="merge"/> copies a folder's contents into an existing folder.</summary>
    public Task CopyOnServerAsync(string source, string destination, bool merge, CancellationToken ct)
    {
        var (s, d) = (PosixShell.Quote(source), PosixShell.Quote(destination));
        return shell.RunCheckedAsync(merge
            ? $"{MergeGuard(source, destination)} cp -a -- {PosixShell.Quote(source + "/.")} {PosixShell.Quote(destination + "/")}"
            : $"{LinkGuard(destination)} cp -a -- {s} {d}", ct);
    }

    private static string LinkGuard(string destination) =>
        $"if [ -L {PosixShell.Quote(destination)} ]; then echo {PosixShell.Quote(new SymlinkRefusedException(destination).Message)} >&2; exit 22; fi;";

    private static string MergeGuard(string source, string destination)
    {
        var (s, d) = (PosixShell.Quote(source), PosixShell.Quote(destination));
        var stopped = PosixShell.Quote($"Something inside “{RemotePath.NameOf(destination)}” is a symbolic link where the copy would write, so Portal stopped before changing anything:");
        return $"{LinkGuard(destination)} ( cd {s} && find . -mindepth 1 -print ) | while IFS= read -r p; do if [ -L {d}/\"$p\" ]; then echo {stopped} \"${{p#./}}\" >&2; exit 23; fi; done || exit 23;";
    }

    /// <summary>Moves one entry to <paramref name="destination"/>; <paramref name="merge"/> moves a folder's contents into an existing folder.</summary>
    public async Task MoveOnServerAsync(string source, string destination, bool merge, CancellationToken ct)
    {
        if (merge)
        {
            await shell.RunCheckedAsync($"{MergeGuard(source, destination)} cp -a -- {PosixShell.Quote(source + "/.")} {PosixShell.Quote(destination + "/")} && rm -rf -- {PosixShell.Quote(source)}", ct).ConfigureAwait(false);
            return;
        }
        if (shell.IsAvailable)
        {
            var d = PosixShell.Quote(destination);
            var folder = PosixShell.Quote($"“{RemotePath.NameOf(destination)}” is a folder, so it can’t be replaced.");
            await shell.RunCheckedAsync($"if [ -d {d} ]; then echo {folder} >&2; exit 21; fi; mv -f -- {PosixShell.Quote(source)} {d}", ct).ConfigureAwait(false);
            return;
        }
        await RefuseLinkAsync(source, ct).ConfigureAwait(false);
        if (await StatAsync(destination, ct).ConfigureAwait(false) is not null) await RefuseLinkAsync(destination, ct).ConfigureAwait(false);
        await ReplaceWithSftpAsync(source, destination, ct).ConfigureAwait(false);
    }

    private async Task<long> SumSizesAsync(string path, CancellationToken ct)
    {
        long total = 0;
        await foreach (var f in sftp.ListDirectoryAsync(path, ct).ConfigureAwait(false))
        {
            if (f.Name is "." or "..") continue;
            total += f.IsDirectory && !f.IsSymbolicLink ? await SumSizesAsync(f.FullName, ct).ConfigureAwait(false) : f.IsRegularFile ? f.Length : 0;
        }
        return total;
    }

    internal static IReadOnlyList<FileEntry> ParseFind(string output)
    {
        var list = new List<FileEntry>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split('\t', 4);
            if (f.Length < 4) continue;
            var kind = f[0] switch
            {
                "d" or "directory" => EntryKind.Directory,
                "f" or "regular file" or "regular empty file" => EntryKind.File,
                "l" or "symbolic link" => EntryKind.LinkToFile,
                _ => EntryKind.Special,
            };
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

    private async Task<bool> LinkTargetIsDirectoryAsync(string path, CancellationToken ct)
    {
        try
        {
            return (await sftp.GetAttributesAsync(path, ct).ConfigureAwait(false)).IsDirectory;
        }
        catch (SshException)
        {
            return false;
        }
    }

    private FileEntry ToEntry(ISftpFile f, bool? linkToDirectory)
    {
        var kind = linkToDirectory is bool dir ? (dir ? EntryKind.LinkToDirectory : EntryKind.LinkToFile)
            : f.IsDirectory ? EntryKind.Directory : f.IsRegularFile ? EntryKind.File : EntryKind.Special;
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
