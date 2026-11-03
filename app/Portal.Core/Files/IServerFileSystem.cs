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

    /// <summary>Changes mode and/or owner of entries, optionally for everything inside folders too.</summary>
    Task SetPermissionsAsync(IReadOnlyList<string> paths, int? mode, string? owner, string? group, bool recursive, CancellationToken ct);

    /// <summary>Total size of a folder and everything inside it.</summary>
    Task<long> DirectorySizeAsync(string path, CancellationToken ct);

    /// <summary>Free and used space of the server's storage.</summary>
    Task<IReadOnlyList<DiskUsage>> DisksAsync(CancellationToken ct);

    /// <summary>Finds entries under <paramref name="root"/> whose name contains <paramref name="text"/>.</summary>
    Task<IReadOnlyList<FileEntry>> SearchAsync(string root, string text, int limit, CancellationToken ct);

    /// <summary>Copies entries into a folder on the server, keeping modes and times.</summary>
    Task CopyOnServerAsync(IReadOnlyList<string> paths, string destinationDirectory, CancellationToken ct);

    /// <summary>Moves entries into a folder on the server.</summary>
    Task MoveOnServerAsync(IReadOnlyList<string> paths, string destinationDirectory, CancellationToken ct);
}
