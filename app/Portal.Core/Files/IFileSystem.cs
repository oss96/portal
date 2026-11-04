namespace Portal.Core.Files;

/// <summary>A browsable file system: this PC or a connected server.</summary>
public interface IFileSystem
{
    /// <summary>Short name shown to the user, such as "This PC" or the server name.</summary>
    string DisplayName { get; }

    /// <summary>True for a server reached over SSH.</summary>
    bool IsRemote { get; }

    /// <summary>The top of the tree: "/" on a server, "" (the drive list) on this PC.</summary>
    string RootPath { get; }

    /// <summary>Joins a folder path and an entry name.</summary>
    string Combine(string directory, string name);

    /// <summary>The containing folder, or null at the root.</summary>
    string? Parent(string path);

    /// <summary>The last segment of a path.</summary>
    string NameOf(string path);

    /// <summary>Breadcrumb segments from the root down to <paramref name="path"/>.</summary>
    IReadOnlyList<PathSegment> Breadcrumbs(string path);

    /// <summary>Turns typed text into an absolute path relative to <paramref name="current"/>, or null if it is not a path.</summary>
    string? ResolveTypedPath(string text, string current);

    /// <summary>Lists a folder.</summary>
    Task<IReadOnlyList<FileEntry>> ListAsync(string path, CancellationToken ct);

    /// <summary>Describes one path, or returns null when it does not exist.</summary>
    Task<FileEntry?> StatAsync(string path, CancellationToken ct);

    /// <summary>Creates a folder.</summary>
    Task CreateDirectoryAsync(string path, CancellationToken ct);

    /// <summary>Renames or moves an entry within this file system.</summary>
    Task RenameAsync(string from, string to, bool overwrite, CancellationToken ct);

    /// <summary>Moves a finished part file into place; without <paramref name="overwrite"/> an existing target makes it fail instead of being replaced.</summary>
    Task PublishAsync(string part, string destination, bool overwrite, CancellationToken ct);

    /// <summary>Deletes entries; folders are deleted with everything inside.</summary>
    Task DeleteAsync(IReadOnlyList<string> paths, CancellationToken ct);

    /// <summary>Copies a file's bytes from <paramref name="offset"/> into <paramref name="destination"/>.</summary>
    Task ReadToAsync(string path, Stream destination, long offset, IProgress<long> progress, CancellationToken ct);

    /// <summary>Writes <paramref name="source"/> to a file, appending when resuming.</summary>
    Task WriteFromAsync(string path, Stream source, bool append, IProgress<long> progress, CancellationToken ct);

    /// <summary>Sets a file's modification time.</summary>
    Task SetModifiedAsync(string path, DateTime modifiedUtc, CancellationToken ct);
}
