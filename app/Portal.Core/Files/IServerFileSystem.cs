using Portal.Core.Connections;

namespace Portal.Core.Files;

/// <summary>A server's file system, with the operations that run on the server itself.</summary>
public interface IServerFileSystem : IFileSystem
{
    /// <summary>The folder the server starts a session in, usually the user's home.</summary>
    string HomeDirectory { get; }

    /// <summary>Names of the server's users and groups.</summary>
    AccountNames Accounts { get; }

    /// <summary>Sets the permission bits of one entry.</summary>
    Task SetModeAsync(string path, int mode, CancellationToken ct);

    /// <summary>True when the account can run commands; SFTP-only accounts can't search, measure disks or copy on the server.</summary>
    bool CanRunCommands { get; }

    /// <summary>Why commands can't run, for the user, or null when they can.</summary>
    string? NoCommandsReason { get; }

    /// <summary>Turns permission bits on and off and/or changes the owner, optionally for everything inside folders too.</summary>
    Task SetPermissionsAsync(IReadOnlyList<string> paths, PermissionDelta? mode, string? owner, string? group, bool recursive, CancellationToken ct);

    /// <summary>Names in <paramref name="folder"/> that are symbolic links, read from one listing without following them.</summary>
    Task<IReadOnlySet<string>> SymbolicLinksInAsync(string folder, CancellationToken ct);

    /// <summary>Total size of a folder and everything inside it.</summary>
    Task<long> DirectorySizeAsync(string path, CancellationToken ct);

    /// <summary>Free and used space of the server's storage.</summary>
    Task<IReadOnlyList<DiskUsage>> DisksAsync(CancellationToken ct);

    /// <summary>Finds entries under <paramref name="root"/> whose name contains <paramref name="text"/>.</summary>
    Task<IReadOnlyList<FileEntry>> SearchAsync(string root, string text, int limit, CancellationToken ct);

    /// <summary>Copies one entry to <paramref name="destination"/>; <paramref name="merge"/> copies a folder's contents into an existing folder.</summary>
    Task CopyOnServerAsync(string source, string destination, bool merge, CancellationToken ct);

    /// <summary>Moves one entry to <paramref name="destination"/>; <paramref name="merge"/> moves a folder's contents into an existing folder.</summary>
    Task MoveOnServerAsync(string source, string destination, bool merge, CancellationToken ct);
}
