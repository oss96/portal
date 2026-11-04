using System.Text;
using Portal.Core.Files;

namespace Portal.Tests;

/// <summary>An in-memory server file system for transfer tests.</summary>
internal sealed class MemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, Node> _nodes = new(StringComparer.Ordinal) { ["/"] = new Node(true, [], DateTime.UtcNow, 0x1ED) };

    private sealed record Node(bool IsDirectory, byte[] Data, DateTime Modified, int Mode);

    public string DisplayName => "Memory";
    public bool IsRemote => true;
    public string RootPath => "/";

    /// <summary>Fails the next write once this many bytes went through, then behaves.</summary>
    public long? FailWriteAfter { get; set; }

    /// <summary>Number of times a write started at a non-zero offset.</summary>
    public int AppendedWrites { get; private set; }

    public void AddDirectory(string path) => _nodes[RemotePath.Normalize(path)] = new Node(true, [], DateTime.UtcNow, 0x1ED);

    public void AddFile(string path, string text, int mode = 0x1A4) => _nodes[RemotePath.Normalize(path)] = new Node(false, Encoding.UTF8.GetBytes(text), new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), mode);

    public string Read(string path) => Encoding.UTF8.GetString(_nodes[path].Data);

    public bool Exists(string path) => _nodes.ContainsKey(path);

    public DateTime ModifiedOf(string path) => _nodes[path].Modified;

    public IEnumerable<string> Paths => _nodes.Keys;

    public string Combine(string directory, string name) => RemotePath.Combine(directory, name);
    public string? Parent(string path) => RemotePath.Parent(path);
    public string NameOf(string path) => RemotePath.NameOf(path);
    public IReadOnlyList<PathSegment> Breadcrumbs(string path) => [new("/", "/")];
    public string? ResolveTypedPath(string text, string current) => RemotePath.Normalize(text);

    public Task<IReadOnlyList<FileEntry>> ListAsync(string path, CancellationToken ct)
    {
        var p = RemotePath.Normalize(path);
        if (!_nodes.TryGetValue(p, out var dir) || !dir.IsDirectory) throw new DirectoryNotFoundException(p);
        IReadOnlyList<FileEntry> list = [.. _nodes.Where(kv => kv.Key != p && RemotePath.Parent(kv.Key) == p).Select(kv => ToEntry(kv.Key, kv.Value))];
        return Task.FromResult(list);
    }

    public Task<FileEntry?> StatAsync(string path, CancellationToken ct) =>
        Task.FromResult(_nodes.TryGetValue(RemotePath.Normalize(path), out var n) ? ToEntry(RemotePath.Normalize(path), n) : null);

    public Task CreateDirectoryAsync(string path, CancellationToken ct)
    {
        AddDirectory(path);
        return Task.CompletedTask;
    }

    public Task RenameAsync(string from, string to, bool overwrite, CancellationToken ct)
    {
        if (!overwrite && _nodes.ContainsKey(to)) throw new IOException("exists");
        _nodes[to] = _nodes[from];
        _nodes.Remove(from);
        return Task.CompletedTask;
    }

    public Task PublishAsync(string part, string destination, bool overwrite, CancellationToken ct) => RenameAsync(part, destination, overwrite, ct);

    public Task DeleteAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        foreach (var p in paths)
            foreach (var key in _nodes.Keys.Where(k => RemotePath.IsWithin(k, p)).ToList()) _nodes.Remove(key);
        return Task.CompletedTask;
    }

    public async Task ReadToAsync(string path, Stream destination, long offset, IProgress<long> progress, CancellationToken ct)
    {
        var data = _nodes[path].Data;
        await destination.WriteAsync(data.AsMemory((int)offset), ct);
        progress.Report(data.Length - offset);
    }

    public async Task WriteFromAsync(string path, Stream source, bool append, IProgress<long> progress, CancellationToken ct)
    {
        if (append) AppendedWrites++;
        var existing = append && _nodes.TryGetValue(path, out var n) ? n.Data : [];
        using var buffer = new MemoryStream();
        buffer.Write(existing);
        var chunk = new byte[4];
        long written = 0;
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            if (FailWriteAfter is long limit && written >= limit)
            {
                FailWriteAfter = null;
                _nodes[path] = new Node(false, buffer.ToArray(), DateTime.UtcNow, 0x1A4);
                throw new IOException("Simulated connection drop.");
            }
            buffer.Write(chunk, 0, read);
            written += read;
            progress.Report(written);
        }
        _nodes[path] = new Node(false, buffer.ToArray(), DateTime.UtcNow, 0x1A4);
    }

    public Task SetModifiedAsync(string path, DateTime modifiedUtc, CancellationToken ct)
    {
        _nodes[path] = _nodes[path] with { Modified = modifiedUtc };
        return Task.CompletedTask;
    }

    private static FileEntry ToEntry(string path, Node n) =>
        new(RemotePath.NameOf(path), path, n.IsDirectory ? EntryKind.Directory : EntryKind.File, n.Data.Length, n.Modified) { Mode = n.Mode };
}
