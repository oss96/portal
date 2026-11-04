using Microsoft.VisualBasic.FileIO;

namespace Portal.Core.Files;

/// <summary>The Windows file system of this PC. The root "" lists the drives.</summary>
public sealed class LocalFileSystem : IFileSystem
{
    /// <inheritdoc />
    public string DisplayName => "This PC";

    /// <inheritdoc />
    public bool IsRemote => false;

    /// <inheritdoc />
    public string RootPath => "";

    /// <inheritdoc />
    public string Combine(string directory, string name) => Path.Combine(directory, name);

    /// <inheritdoc />
    public string? Parent(string path)
    {
        if (path.Length == 0) return null;
        var parent = Path.GetDirectoryName(path.TrimEnd('\\'));
        return string.IsNullOrEmpty(parent) || IsDriveRoot(path) ? "" : parent;
    }

    /// <inheritdoc />
    public string NameOf(string path)
    {
        if (path.Length == 0) return DisplayName;
        if (IsDriveRoot(path)) return path.TrimEnd('\\');
        return Path.GetFileName(path.TrimEnd('\\'));
    }

    /// <inheritdoc />
    public IReadOnlyList<PathSegment> Breadcrumbs(string path)
    {
        var list = new List<PathSegment> { new(DisplayName, "") };
        if (path.Length == 0) return list;
        var root = Path.GetPathRoot(path) ?? path;
        list.Add(new PathSegment(root.TrimEnd('\\'), root));
        var current = root;
        foreach (var seg in path[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, seg);
            list.Add(new PathSegment(seg, current));
        }
        return list;
    }

    /// <inheritdoc />
    public string? ResolveTypedPath(string text, string current)
    {
        var t = Environment.ExpandEnvironmentVariables(text.Trim().Trim('"'));
        if (t.Length == 0) return null;
        if (t.Equals(DisplayName, StringComparison.OrdinalIgnoreCase)) return "";
        if (t.Length == 2 && t[1] == ':') t += "\\";
        try
        {
            var full = Path.IsPathRooted(t) ? Path.GetFullPath(t) : current.Length == 0 ? null : Path.GetFullPath(Path.Combine(current, t));
            return full;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<FileEntry>> ListAsync(string path, CancellationToken ct) => Task.Run(() =>
    {
        if (path.Length == 0) return (IReadOnlyList<FileEntry>)Drives();
        var dir = new DirectoryInfo(path);
        if (!dir.Exists) throw new DirectoryNotFoundException($"“{path}” doesn’t exist.");
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false };
        var list = new List<FileEntry>();
        foreach (var info in dir.EnumerateFileSystemInfos("*", options))
        {
            ct.ThrowIfCancellationRequested();
            list.Add(ToEntry(info));
        }
        return list;
    }, ct);

    /// <inheritdoc />
    public Task<FileEntry?> StatAsync(string path, CancellationToken ct) => Task.Run(() =>
    {
        if (path.Length == 0) return null;
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        return info.Exists ? ToEntry(info) : null;
    }, ct);

    /// <inheritdoc />
    public Task CreateDirectoryAsync(string path, CancellationToken ct) => Task.Run(() => Directory.CreateDirectory(path), ct);

    /// <inheritdoc />
    public Task RenameAsync(string from, string to, bool overwrite, CancellationToken ct) => Task.Run(() =>
    {
        if (Directory.Exists(from)) Directory.Move(from, to);
        else File.Move(from, to, overwrite);
    }, ct);

    /// <inheritdoc />
    public Task PublishAsync(string part, string destination, bool overwrite, CancellationToken ct) =>
        Task.Run(() => File.Move(part, destination, overwrite), ct);

    /// <summary>Moves the entries to the Recycle Bin.</summary>
    public Task DeleteAsync(IReadOnlyList<string> paths, CancellationToken ct) => Task.Run(() =>
    {
        foreach (var p in paths)
        {
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(p)) FileSystem.DeleteDirectory(p, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            else if (File.Exists(p)) FileSystem.DeleteFile(p, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
    }, ct);

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>A Windows-safe version of a server name: characters Windows forbids (\ / : * ? " &lt; &gt; | and control characters) become "_",
    /// trailing dots and spaces are dropped, and device names and "." or ".." get a leading "_".</summary>
    public static string SafeName(string name)
    {
        var chars = name.Select(c => c < 32 || "\\/:*?\"<>|".Contains(c) ? '_' : c).ToArray();
        var safe = new string(chars).TrimEnd('.', ' ');
        if (safe.Length == 0) return "_";
        var stem = safe.Split('.')[0];
        return ReservedNames.Contains(stem) ? "_" + safe : safe;
    }

    /// <summary>Throws unless <paramref name="path"/> resolves to a place inside <paramref name="folder"/>.</summary>
    public static void EnsureInside(string folder, string path)
    {
        var root = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
        if (!Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"“{Path.GetFileName(path)}” would land outside {folder}, so Portal didn’t write it.");
    }

    /// <summary>Deletes a file outright, skipping the Recycle Bin. Used for transfer leftovers.</summary>
    public static void DeletePermanently(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    /// <inheritdoc />
    public async Task ReadToAsync(string path, Stream destination, long offset, IProgress<long> progress, CancellationToken ct)
    {
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, useAsync: true);
        source.Seek(offset, SeekOrigin.Begin);
        await StreamCopy.CopyAsync(source, destination, progress, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task WriteFromAsync(string path, Stream source, bool append, IProgress<long> progress, CancellationToken ct)
    {
        await using var target = new FileStream(path, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
        await StreamCopy.CopyAsync(source, target, progress, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task SetModifiedAsync(string path, DateTime modifiedUtc, CancellationToken ct) => Task.Run(() =>
    {
        if (Directory.Exists(path)) Directory.SetLastWriteTimeUtc(path, modifiedUtc);
        else File.SetLastWriteTimeUtc(path, modifiedUtc);
    }, ct);

    /// <summary>Opens a local file for reading at <paramref name="offset"/>.</summary>
    public static Stream OpenRead(string path, long offset)
    {
        var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, useAsync: true);
        s.Seek(offset, SeekOrigin.Begin);
        return s;
    }

    /// <summary>Opens a local file for writing, appending when resuming.</summary>
    public static Stream OpenWrite(string path, bool append) =>
        new FileStream(path, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);

    private static bool IsDriveRoot(string path) =>
        string.Equals(Path.GetPathRoot(path)?.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private static List<FileEntry> Drives()
    {
        var list = new List<FileEntry>();
        foreach (var d in DriveInfo.GetDrives())
        {
            if (!d.IsReady) continue;
            list.Add(new FileEntry(d.Name.TrimEnd('\\'), d.Name, EntryKind.Directory, 0, DateTime.MinValue));
        }
        return list;
    }

    private static FileEntry ToEntry(FileSystemInfo info)
    {
        var isDir = info is DirectoryInfo;
        var hidden = (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
        var kind = info.LinkTarget is not null ? (isDir ? EntryKind.LinkToDirectory : EntryKind.LinkToFile) : isDir ? EntryKind.Directory : EntryKind.File;
        return new FileEntry(info.Name, info.FullName, kind, info is FileInfo f ? f.Length : 0, info.LastWriteTimeUtc) { IsHidden = hidden };
    }
}
