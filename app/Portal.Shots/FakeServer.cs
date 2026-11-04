using Portal.Core.Connections;
using Portal.Core.Files;

namespace Portal.Shots;

/// <summary>An in-memory Unraid-like server with slow, observable transfers.</summary>
internal sealed class FakeServer : IServerFileSystem
{
    private const long Kb = 1024, Mb = Kb * 1024, Gb = Mb * 1024;
    private readonly Dictionary<string, FileEntry> _entries = new(StringComparer.Ordinal);

    public FakeServer()
    {
        Dir("/", "root", "root", 0x1ED, "2026-10-01");
        foreach (var d in new[] { "/boot", "/mnt", "/mnt/user", "/root", "/etc", "/var" }) Dir(d, "root", "root", 0x1ED, "2026-10-28");
        Dir("/boot/config", "root", "root", 0x1ED, "2026-09-14");
        File("/boot/config/go", 1180, "root", "root", 0x1ED, "2026-09-14");
        File("/boot/config/ident.cfg", 912, "root", "root", 0x1A4, "2026-09-14");
        foreach (var share in new[] { "appdata", "backups", "downloads", "media" }) Dir($"/mnt/user/{share}", "nobody", "users", 0x1FF, "2026-11-02");
        foreach (var app in new[] { "forgejo", "jellyfin", "kaneo", "swag", "tailscale" }) Dir($"/mnt/user/appdata/{app}", "nobody", "users", 0x1FF, "2026-10-20");
        File("/mnt/user/appdata/jellyfin/log.txt", (long)(3.2 * Mb), "nobody", "users", 0x1B6, "2026-11-03T13:58");
        foreach (var d in new[] { "Movies", "TV", "Music" }) Dir($"/mnt/user/media/{d}", "nobody", "users", 0x1FF, "2026-07-01");
        foreach (var (movie, size) in new[] { ("Arrival (2016)", 14.2), ("Blade Runner 2049 (2017)", 22.8), ("Dune (2021)", 19.6), ("Past Lives (2023)", 8.4) })
        {
            Dir($"/mnt/user/media/Movies/{movie}", "nobody", "users", 0x1FF, "2026-06-07");
            File($"/mnt/user/media/Movies/{movie}/{movie}.mkv", (long)(size * Gb), "nobody", "users", 0x1B6, "2026-06-07");
        }
        Dir("/mnt/user/downloads/complete", "nobody", "users", 0x1FF, "2026-11-01");
        Dir("/mnt/user/downloads/incomplete", "nobody", "users", 0x1FF, "2026-11-02T23:59");
        File("/mnt/user/downloads/ubuntu-26.04-desktop-amd64.iso", (long)(6.1 * Gb), "nobody", "users", 0x1B6, "2026-10-24");
        File("/mnt/user/downloads/Dune Part Two (2024).mkv", (long)(18.4 * Gb), "nobody", "users", 0x1B6, "2026-10-02");
        File("/mnt/user/downloads/.DS_Store", 6 * Kb, "nobody", "users", 0x1B6, "2026-09-01");
        File("/mnt/user/backups/flash-2026-10-01.zip", 412 * Mb, "root", "root", 0x1A4, "2026-10-01");
        File("/mnt/user/backups/appdata-2026-10-28.tar.zst", (long)(9.8 * Gb), "root", "root", 0x1A4, "2026-10-28");
        File("/root/notes.txt", 2 * Kb, "root", "root", 0x1A4, "2026-10-30");
    }

    /// <summary>Names whose upload fails part-way once.</summary>
    public HashSet<string> FailOnce { get; } = [];

    public string DisplayName => "Tower";
    public bool IsRemote => true;
    public string RootPath => "/";
    public string HomeDirectory => "/root";
    public AccountNames Accounts { get; } = AccountNames.Parse($"root:x:0:0\nnobody:x:99:100\noss:x:1000:100\n{AccountNames.Separator}\nroot:x:0:\nusers:x:100:\n");

    public string Combine(string directory, string name) => RemotePath.Combine(directory, name);
    public string? Parent(string path) => RemotePath.Parent(path);
    public string NameOf(string path) => RemotePath.NameOf(path);

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

    public string? ResolveTypedPath(string text, string current) => RemotePath.Normalize(text.StartsWith('/') ? text : RemotePath.Combine(current, text));

    public Task<IReadOnlyList<FileEntry>> ListAsync(string path, CancellationToken ct)
    {
        var p = RemotePath.Normalize(path);
        IReadOnlyList<FileEntry> list = [.. _entries.Values.Where(e => e.FullPath != p && RemotePath.Parent(e.FullPath) == p)];
        return Task.FromResult(list);
    }

    public Task<FileEntry?> StatAsync(string path, CancellationToken ct) => Task.FromResult(_entries.GetValueOrDefault(RemotePath.Normalize(path)));

    public Task CreateDirectoryAsync(string path, CancellationToken ct)
    {
        Dir(path, "root", "root", 0x1ED, DateTime.UtcNow.ToString("s"));
        return Task.CompletedTask;
    }

    public Task RenameAsync(string from, string to, bool overwrite, CancellationToken ct)
    {
        var e = _entries[from];
        _entries.Remove(from);
        _entries[to] = e with { Name = RemotePath.NameOf(to), FullPath = to };
        return Task.CompletedTask;
    }

    public Task DeleteAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        foreach (var p in paths)
            foreach (var k in _entries.Keys.Where(k => RemotePath.IsWithin(k, p)).ToList()) _entries.Remove(k);
        return Task.CompletedTask;
    }

    public async Task ReadToAsync(string path, Stream destination, long offset, IProgress<long> progress, CancellationToken ct)
    {
        var chunk = new byte[64 * 1024];
        for (long done = 0; done < 8 * Mb; done += chunk.Length)
        {
            await Task.Delay(15, ct);
            await destination.WriteAsync(chunk, ct);
            progress.Report(done);
        }
    }

    public async Task WriteFromAsync(string path, Stream source, bool append, IProgress<long> progress, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        long done = 0;
        int read;
        var name = RemotePath.NameOf(path);
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await Task.Delay(15, ct);
            done += read;
            progress.Report(done);
            if (done > 5 * Mb && FailOnce.Any(f => name.Contains(f, StringComparison.Ordinal)))
            {
                FailOnce.RemoveWhere(f => name.Contains(f, StringComparison.Ordinal));
                throw new IOException("The connection to Tower dropped.");
            }
        }
        File(path, done, "root", "root", 0x1A4, DateTime.UtcNow.ToString("s"));
    }

    public Task SetModifiedAsync(string path, DateTime modifiedUtc, CancellationToken ct) => Task.CompletedTask;
    public Task SetModeAsync(string path, int mode, CancellationToken ct) => Task.CompletedTask;
    public bool CanRunCommands => true;
    public string? NoCommandsReason => null;
    public Task<IReadOnlySet<string>> SymbolicLinksInAsync(string folder, CancellationToken ct) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    public Task SetPermissionsAsync(IReadOnlyList<string> paths, PermissionDelta? mode, string? owner, string? group, bool recursive, CancellationToken ct) => Task.CompletedTask;
    public Task<long> DirectorySizeAsync(string path, CancellationToken ct) => Task.FromResult(_entries.Values.Where(e => RemotePath.IsWithin(e.FullPath, path)).Sum(e => e.Size));

    public Task<IReadOnlyList<DiskUsage>> DisksAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<DiskUsage>>(
    [
        new("/mnt/user", 22 * 1024 * Gb, (long)(18.9 * 1024 * Gb), (long)(3.1 * 1024 * Gb)),
        new("/mnt/cache", 1000 * Gb, 440 * Gb, 560 * Gb),
        new("/boot", 32 * Gb, 1 * Gb, 31 * Gb),
    ]);

    public Task<IReadOnlyList<FileEntry>> SearchAsync(string root, string text, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FileEntry>>([.. _entries.Values.Where(e => RemotePath.IsWithin(e.FullPath, root) && e.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).Take(limit)]);

    public Task CopyOnServerAsync(string source, string destination, bool merge, CancellationToken ct) => Task.CompletedTask;
    public Task MoveOnServerAsync(string source, string destination, bool merge, CancellationToken ct) => Task.CompletedTask;

    private void Dir(string path, string owner, string group, int mode, string when) =>
        _entries[path] = new FileEntry(RemotePath.NameOf(path), path, EntryKind.Directory, 0, DateTime.Parse(when).ToUniversalTime()) { Mode = mode, Owner = owner, Group = group };

    private void File(string path, long size, string owner, string group, int mode, string when) =>
        _entries[path] = new FileEntry(RemotePath.NameOf(path), path, EntryKind.File, size, DateTime.Parse(when).ToUniversalTime()) { Mode = mode, Owner = owner, Group = group, IsHidden = RemotePath.NameOf(path).StartsWith('.') };
}

/// <summary>A session over <see cref="FakeServer"/>.</summary>
internal sealed class FakeSession(ServerProfile profile, FakeServer server) : IServerSession
{
    public ServerProfile Profile => profile;
    public string HostName => "tower";
    public int Port => 22;
    public string User => "root";
    public TimeSpan Latency => TimeSpan.FromMilliseconds(14);
    public IServerFileSystem Files => server;
    public IServerFileSystem TransferFiles => server;
    public bool IsConnected => true;
    public event EventHandler? ConnectionLost { add { } remove { } }
    public Task ReconnectAsync(CancellationToken ct) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
