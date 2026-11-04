using Portal.Core.Files;

namespace Portal.Core.Transfers;

/// <summary>Which way a transfer goes.</summary>
public enum TransferDirection
{
    /// <summary>From this PC to the server.</summary>
    Upload,
    /// <summary>From the server to this PC.</summary>
    Download,
}

/// <summary>Where a transfer is in its life.</summary>
public enum TransferStatus
{
    /// <summary>Waiting for a free slot.</summary>
    Queued,
    /// <summary>Copying.</summary>
    Running,
    /// <summary>Stopped by the user; resumes where it stopped.</summary>
    Paused,
    /// <summary>Finished.</summary>
    Done,
    /// <summary>Stopped by an error; retry resumes where it stopped.</summary>
    Failed,
    /// <summary>Cancelled by the user; partial data was removed.</summary>
    Cancelled,
}

/// <summary>A group of items started by one upload or download action.</summary>
public sealed class TransferBatch
{
    internal TransferBatch(TransferDirection direction, string destinationDirectory, string destinationLabel, int count)
    {
        Direction = direction;
        DestinationDirectory = destinationDirectory;
        DestinationLabel = destinationLabel;
        Count = count;
    }

    /// <summary>Upload or download.</summary>
    public TransferDirection Direction { get; }

    /// <summary>The folder the items go into.</summary>
    public string DestinationDirectory { get; }

    /// <summary>"Tower" or "This PC".</summary>
    public string DestinationLabel { get; }

    /// <summary>Number of items in the batch.</summary>
    public int Count { get; }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<IReadOnlySet<string>>> _links = new(StringComparer.Ordinal);

    internal Task<IReadOnlySet<string>> LinksInAsync(IServerFileSystem server, string folder, CancellationToken ct) =>
        _links.GetOrAdd(folder, f => server.SymbolicLinksInAsync(f, CancellationToken.None)).WaitAsync(ct);

    internal void ClearLinkCache() => _links.Clear();
}

/// <summary>One file or folder being transferred.</summary>
public sealed class TransferItem
{
    private readonly Queue<(DateTime At, long Bytes)> _samples = new();

    internal TransferItem(TransferBatch batch, IFileSystem from, IFileSystem to, FileEntry source, string destinationName, bool replace)
    {
        Batch = batch;
        From = from;
        To = to;
        Source = source;
        DestinationName = destinationName;
        Replace = replace;
        PlannedPath = to.Combine(batch.DestinationDirectory, destinationName);
        TotalBytes = source.IsDirectory ? 0 : source.Size;
        FilesTotal = source.IsDirectory ? 0 : 1;
    }

    /// <summary>Stable id for the UI.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>The batch this item belongs to.</summary>
    public TransferBatch Batch { get; }

    /// <summary>Upload or download.</summary>
    public TransferDirection Direction => Batch.Direction;

    /// <summary>What is being copied.</summary>
    public FileEntry Source { get; }

    /// <summary>Name the item gets at the destination.</summary>
    public string DestinationName { get; internal set; }

    /// <summary>Full destination path of the item.</summary>
    public string DestinationPath => To.Combine(Batch.DestinationDirectory, DestinationName);

    /// <summary>True when an existing file is overwritten or an existing folder is merged into.</summary>
    public bool Replace { get; }

    /// <summary>Total bytes, known once folders have been scanned.</summary>
    public long TotalBytes { get; internal set; }

    /// <summary>Bytes copied so far.</summary>
    public long BytesDone { get; internal set; }

    /// <summary>Number of files to copy.</summary>
    public int FilesTotal { get; internal set; }

    /// <summary>Number of files copied.</summary>
    public int FilesDone { get; internal set; }

    /// <summary>The file inside a folder being copied right now.</summary>
    public string? CurrentFile { get; internal set; }

    /// <summary>Current state.</summary>
    public TransferStatus Status { get; internal set; } = TransferStatus.Queued;

    /// <summary>Why the item failed.</summary>
    public string? Error { get; internal set; }

    /// <summary>Names left out because they are symbolic links on the server.</summary>
    public IReadOnlyList<string> SkippedLinks => Skipped;

    /// <summary>Recent speed in bytes per second.</summary>
    public double BytesPerSecond { get; private set; }

    /// <summary>True when the item was failed or paused before and continues from a partial file.</summary>
    public bool Resuming { get; internal set; }

    /// <summary>Fraction done, 0 to 1.</summary>
    public double Fraction => TotalBytes <= 0 ? (Status == TransferStatus.Done ? 1 : 0) : Math.Clamp((double)BytesDone / TotalBytes, 0, 1);

    internal IFileSystem From { get; }
    internal IFileSystem To { get; }
    internal TransferPlan? Plan { get; set; }
    internal HashSet<string> Completed { get; } = [];
    internal CancellationTokenSource? Cancellation { get; set; }
    internal bool PauseRequested { get; set; }
    internal string? CurrentPartPath { get; set; }
    internal string? ResumablePart { get; set; }
    internal List<string> Skipped { get; } = [];

    internal string PlannedPath { get; }

    internal void AddSkipped(string destinationPath)
    {
        var root = DestinationPath;
        var relative = destinationPath.StartsWith(root + "/", StringComparison.Ordinal) ? destinationPath[(root.Length + 1)..] : To.NameOf(destinationPath);
        lock (Skipped)
            if (!Skipped.Contains(relative)) Skipped.Add(relative);
    }

    internal void Sample()
    {
        var now = DateTime.UtcNow;
        lock (_samples)
        {
            _samples.Enqueue((now, BytesDone));
            while (_samples.Count > 2 && now - _samples.Peek().At > TimeSpan.FromSeconds(3)) _samples.Dequeue();
            var (at, bytes) = _samples.Peek();
            var span = (now - at).TotalSeconds;
            if (span > 0.3) BytesPerSecond = (BytesDone - bytes) / span;
        }
    }

    internal void ResetSpeed()
    {
        lock (_samples) _samples.Clear();
        BytesPerSecond = 0;
    }
}

internal sealed record PlannedFile(string SourcePath, string DestinationPath, FileEntry Entry);

internal sealed record TransferPlan(IReadOnlyList<string> Directories, IReadOnlyList<PlannedFile> Files);
