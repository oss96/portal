using Portal.Core.Connections;
using Portal.Core.Files;
using Portal.Core.Settings;

namespace Portal.Core.Transfers;

/// <summary>What to do about an existing entry with the same name.</summary>
public enum ConflictChoice
{
    /// <summary>Overwrite the file, or merge into the folder.</summary>
    Replace,
    /// <summary>Save under a numbered name.</summary>
    KeepBoth,
    /// <summary>Leave the existing entry alone.</summary>
    Skip,
    /// <summary>Stop the whole action; nothing is queued.</summary>
    Cancel,
}

/// <summary>A conflict the user is asked about.</summary>
public sealed record ConflictQuestion(FileEntry Incoming, FileEntry Existing, TransferDirection Direction, string DestinationLabel, string KeepBothName, int Remaining)
{
    /// <summary>False when a file meets a folder of the same name; then only Keep both and Skip make sense.</summary>
    public bool CanReplace => Incoming.IsDirectory == Existing.IsDirectory;

    /// <summary>Label for the incoming copy when it isn't an upload or download, such as "Being moved".</summary>
    public string? IncomingLabel { get; init; }
}

/// <summary>The user's answer to a <see cref="ConflictQuestion"/>.</summary>
public sealed record ConflictAnswer(ConflictChoice Choice, bool ApplyToRest);

/// <summary>Queues uploads and downloads and runs a limited number at once.</summary>
public sealed class TransferEngine(Func<int> maxParallel)
{
    private readonly object _gate = new();
    private readonly List<TransferItem> _items = [];

    /// <summary>Raised when items are added or removed or change status. May fire on any thread.</summary>
    public event Action? Changed;

    /// <summary>Raised when every item of a batch has finished, failed or been cancelled. May fire on any thread.</summary>
    public event Action<TransferBatch, IReadOnlyList<TransferItem>>? BatchFinished;

    /// <summary>A copy of the current list, oldest first.</summary>
    public IReadOnlyList<TransferItem> Snapshot()
    {
        lock (_gate) return [.. _items];
    }

    /// <summary>True while anything is queued, running or paused.</summary>
    public bool HasUnfinished
    {
        get { lock (_gate) return _items.Any(i => i.Status is TransferStatus.Queued or TransferStatus.Running or TransferStatus.Paused); }
    }

    /// <summary>Checks for name conflicts, asks when needed, then queues the sources. Returns null when the user stopped it or nothing was left to do.</summary>
    public async Task<TransferBatch?> EnqueueAsync(IFileSystem from, IReadOnlyList<FileEntry> sources, IFileSystem to, string destinationDirectory,
        ConflictPolicy policy, Func<ConflictQuestion, Task<ConflictAnswer>> ask, CancellationToken ct)
    {
        if (from.IsRemote == to.IsRemote) throw new InvalidOperationException("Transfers go between this PC and a server.");
        var direction = to.IsRemote ? TransferDirection.Upload : TransferDirection.Download;
        var names = to.IsRemote ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var existing = new Dictionary<string, FileEntry>(names);
        foreach (var e in await to.ListAsync(destinationDirectory, ct).ConfigureAwait(false)) existing.TryAdd(e.Name, e);
        var taken = new HashSet<string>(existing.Keys, names);
        var conflicts = sources.Count(s => existing.ContainsKey(s.Name));
        ConflictChoice? standing = policy switch
        {
            ConflictPolicy.Replace => ConflictChoice.Replace,
            ConflictPolicy.Skip => ConflictChoice.Skip,
            ConflictPolicy.KeepBoth => ConflictChoice.KeepBoth,
            _ => null,
        };
        var planned = new List<(FileEntry Source, string Name, bool Replace)>();
        foreach (var source in sources)
        {
            if (!existing.TryGetValue(source.Name, out var current))
            {
                var name = taken.Contains(source.Name) ? UniqueName(taken, source.Name) : source.Name;
                planned.Add((source, name, false));
                taken.Add(name);
                continue;
            }
            conflicts--;
            var keepBothName = UniqueName(taken, source.Name);
            var question = new ConflictQuestion(source, current, direction, to.DisplayName, keepBothName, conflicts);
            var choice = standing;
            if (choice is null || (choice == ConflictChoice.Replace && !question.CanReplace))
            {
                var answer = await ask(question).ConfigureAwait(false);
                choice = answer.Choice;
                if (answer.ApplyToRest && question.CanReplace) standing = answer.Choice;
            }
            if (choice == ConflictChoice.Replace && !question.CanReplace) choice = ConflictChoice.KeepBoth;
            switch (choice)
            {
                case ConflictChoice.Cancel:
                    return null;
                case ConflictChoice.Skip:
                    continue;
                case ConflictChoice.KeepBoth:
                    planned.Add((source, keepBothName, false));
                    taken.Add(keepBothName);
                    break;
                default:
                    planned.Add((source, source.Name, true));
                    break;
            }
        }
        if (planned.Count == 0) return null;
        var batch = new TransferBatch(direction, destinationDirectory, to.DisplayName, planned.Count);
        lock (_gate)
        {
            foreach (var (source, name, replace) in planned) _items.Add(new TransferItem(batch, from, to, source, name, replace));
        }
        Changed?.Invoke();
        Pump();
        return batch;
    }

    /// <summary>Stops a running or queued item but keeps its partial data for <see cref="Resume"/>.</summary>
    public void Pause(TransferItem item)
    {
        lock (_gate)
        {
            if (item.Status == TransferStatus.Running) { item.PauseRequested = true; item.Cancellation?.Cancel(); }
            else if (item.Status == TransferStatus.Queued) item.Status = TransferStatus.Paused;
        }
        Changed?.Invoke();
    }

    /// <summary>Queues a paused or failed item again; it continues where it stopped.</summary>
    public void Resume(TransferItem item)
    {
        lock (_gate)
        {
            if (item.Status is not (TransferStatus.Paused or TransferStatus.Failed)) return;
            item.Resuming = item.BytesDone > 0;
            item.Error = null;
            item.Status = TransferStatus.Queued;
        }
        Changed?.Invoke();
        Pump();
    }

    /// <summary>Stops an item for good and removes its partial data.</summary>
    public void Cancel(TransferItem item)
    {
        var cleanup = false;
        lock (_gate)
        {
            if (item.Status == TransferStatus.Running) { item.PauseRequested = false; item.Cancellation?.Cancel(); }
            else if (item.Status is TransferStatus.Queued or TransferStatus.Paused or TransferStatus.Failed)
            {
                item.Status = TransferStatus.Cancelled;
                cleanup = true;
            }
        }
        if (cleanup) _ = Task.Run(() => FinishAsync(item));
        Changed?.Invoke();
    }

    /// <summary>Cancels every unfinished item that reads from or writes to <paramref name="fileSystem"/>, waits up to <paramref name="cleanup"/> for their partial files to be removed, and returns how many.</summary>
    public async Task<int> CancelAllUsingAsync(IFileSystem fileSystem, TimeSpan cleanup)
    {
        var items = Snapshot().Where(i => (i.From == fileSystem || i.To == fileSystem)
            && i.Status is TransferStatus.Queued or TransferStatus.Running or TransferStatus.Paused or TransferStatus.Failed).ToList();
        foreach (var i in items) Cancel(i);
        var deadline = DateTime.UtcNow + cleanup;
        while (DateTime.UtcNow < deadline && items.Any(i => i.Status != TransferStatus.Cancelled || i.CurrentPartPath is not null))
            await Task.Delay(50).ConfigureAwait(false);
        return items.Count;
    }

    /// <summary>Pauses or resumes everything at once.</summary>
    public void PauseAll()
    {
        foreach (var i in Snapshot().Where(i => i.Status is TransferStatus.Running or TransferStatus.Queued)) Pause(i);
    }

    /// <summary>Removes finished and cancelled items from the list.</summary>
    public void ClearFinished()
    {
        lock (_gate) _items.RemoveAll(i => i.Status is TransferStatus.Done or TransferStatus.Cancelled);
        Changed?.Invoke();
    }

    /// <summary>A free name such as "photo (1).jpg" that is not in <paramref name="taken"/>.</summary>
    public static string UniqueName(ISet<string> taken, string name)
    {
        var dot = name.LastIndexOf('.');
        var stem = dot > 0 ? name[..dot] : name;
        var ext = dot > 0 ? name[dot..] : "";
        for (var i = 1; ; i++)
        {
            var candidate = $"{stem} ({i}){ext}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    private void Pump()
    {
        lock (_gate)
        {
            var running = _items.Count(i => i.Status == TransferStatus.Running);
            foreach (var item in _items)
            {
                if (running >= maxParallel()) break;
                if (item.Status != TransferStatus.Queued) continue;
                item.Status = TransferStatus.Running;
                item.PauseRequested = false;
                item.Cancellation = new CancellationTokenSource();
                running++;
                var token = item.Cancellation.Token;
                _ = Task.Run(() => RunAsync(item, token), CancellationToken.None);
            }
        }
    }

    private async Task RunAsync(TransferItem item, CancellationToken ct)
    {
        Changed?.Invoke();
        try
        {
            await ExecuteAsync(item, ct).ConfigureAwait(false);
            item.Status = TransferStatus.Done;
        }
        catch (OperationCanceledException) when (item.PauseRequested)
        {
            item.ResumablePart = item.CurrentPartPath;
            item.Status = TransferStatus.Paused;
        }
        catch (OperationCanceledException)
        {
            item.Status = TransferStatus.Cancelled;
        }
        catch (Exception e)
        {
            item.ResumablePart = item.CurrentPartPath;
            item.Error = FriendlyError.ForOperation(e);
            item.Status = TransferStatus.Failed;
        }
        await FinishAsync(item).ConfigureAwait(false);
        Pump();
    }

    private async Task FinishAsync(TransferItem item)
    {
        item.ResetSpeed();
        item.CurrentFile = null;
        if (item.Status == TransferStatus.Cancelled) await RemovePartAsync(item).ConfigureAwait(false);
        Changed?.Invoke();
        if (item.Status is not (TransferStatus.Done or TransferStatus.Failed or TransferStatus.Cancelled)) return;
        List<TransferItem> members;
        lock (_gate)
        {
            members = [.. _items.Where(i => i.Batch == item.Batch)];
            if (members.Any(i => i.Status is TransferStatus.Queued or TransferStatus.Running or TransferStatus.Paused)) return;
        }
        BatchFinished?.Invoke(item.Batch, members);
    }

    private static async Task RemovePartAsync(TransferItem item)
    {
        var part = item.CurrentPartPath;
        if (part is null) return;
        try
        {
            if (item.To.IsRemote) await item.To.DeleteAsync([part], CancellationToken.None).ConfigureAwait(false);
            else LocalFileSystem.DeletePermanently(part);
        }
        catch (Exception)
        {
        }
        item.CurrentPartPath = null;
    }

    private static async Task ExecuteAsync(TransferItem item, CancellationToken ct)
    {
        item.Plan ??= await PlanAsync(item, ct).ConfigureAwait(false);
        item.TotalBytes = item.Plan.Files.Sum(f => f.Entry.Size);
        item.FilesTotal = item.Plan.Files.Count;
        foreach (var dir in item.Plan.Directories)
        {
            if (await item.To.StatAsync(dir, ct).ConfigureAwait(false) is null) await item.To.CreateDirectoryAsync(dir, ct).ConfigureAwait(false);
        }
        item.BytesDone = item.Plan.Files.Where(f => item.Completed.Contains(f.DestinationPath)).Sum(f => f.Entry.Size);
        item.FilesDone = item.Completed.Count;
        foreach (var file in item.Plan.Files)
        {
            if (item.Completed.Contains(file.DestinationPath)) continue;
            ct.ThrowIfCancellationRequested();
            item.CurrentFile = file.Entry.Name;
            await CopyFileAsync(item, file, ct).ConfigureAwait(false);
            item.Completed.Add(file.DestinationPath);
            item.FilesDone = item.Completed.Count;
        }
    }

    private static async Task<TransferPlan> PlanAsync(TransferItem item, CancellationToken ct)
    {
        var destination = item.DestinationPath;
        if (!item.Source.IsDirectory) return new TransferPlan([], [new PlannedFile(item.Source.FullPath, destination, item.Source)]);
        var dirs = new List<string> { destination };
        var files = new List<PlannedFile>();
        var pending = new Stack<(string Source, string Destination)>();
        pending.Push((item.Source.FullPath, destination));
        while (pending.Count > 0)
        {
            var (src, dst) = pending.Pop();
            var names = new HashSet<string>(item.To.IsRemote ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            foreach (var e in await item.From.ListAsync(src, ct).ConfigureAwait(false))
            {
                var name = names.Contains(e.Name) ? UniqueName(names, e.Name) : e.Name;
                names.Add(name);
                var target = item.To.Combine(dst, name);
                switch (e.Kind)
                {
                    case EntryKind.Directory:
                        dirs.Add(target);
                        pending.Push((e.FullPath, target));
                        break;
                    case EntryKind.File:
                    case EntryKind.LinkToFile:
                        files.Add(new PlannedFile(e.FullPath, target, e));
                        break;
                }
            }
        }
        return new TransferPlan(dirs, files);
    }

    private static async Task CopyFileAsync(TransferItem item, PlannedFile file, CancellationToken ct)
    {
        var to = item.To;
        var name = to.NameOf(file.DestinationPath);
        var folder = to.Parent(file.DestinationPath) ?? to.RootPath;
        var part = to.Combine(folder, to.IsRemote ? $".{name}.portal-part" : $"{name}.portal-part");
        item.CurrentPartPath = part;

        var offset = 0L;
        if (part == item.ResumablePart && await SourceUnchangedAsync(item, file, ct).ConfigureAwait(false)
            && await to.StatAsync(part, ct).ConfigureAwait(false) is { IsDirectory: false } partial && partial.Size <= file.Entry.Size)
            offset = partial.Size;
        item.ResumablePart = null;
        var before = item.BytesDone;
        var progress = new SyncProgress(n => { item.BytesDone = before + offset + n; item.Sample(); });
        progress.Report(0);

        if (to.IsRemote)
        {
            await using var source = LocalFileSystem.OpenRead(file.SourcePath, offset);
            await to.WriteFromAsync(part, source, offset > 0, progress, ct).ConfigureAwait(false);
        }
        else
        {
            await using var target = LocalFileSystem.OpenWrite(part, offset > 0);
            await item.From.ReadToAsync(file.SourcePath, target, offset, progress, ct).ConfigureAwait(false);
        }

        var replaced = item.Replace ? await to.StatAsync(file.DestinationPath, ct).ConfigureAwait(false) : null;
        await to.RenameAsync(part, file.DestinationPath, overwrite: true, ct).ConfigureAwait(false);
        item.CurrentPartPath = null;
        await BestEffort(() => to.SetModifiedAsync(file.DestinationPath, file.Entry.ModifiedUtc, CancellationToken.None)).ConfigureAwait(false);
        if (to is IServerFileSystem remote && replaced?.Mode is int mode)
            await BestEffort(() => remote.SetModeAsync(file.DestinationPath, mode, CancellationToken.None)).ConfigureAwait(false);
        item.BytesDone = before + file.Entry.Size;
    }

    private static async Task<bool> SourceUnchangedAsync(TransferItem item, PlannedFile file, CancellationToken ct)
    {
        var now = await item.From.StatAsync(file.SourcePath, ct).ConfigureAwait(false);
        return now is not null && now.Size == file.Entry.Size && now.ModifiedUtc == file.Entry.ModifiedUtc;
    }

    private static async Task BestEffort(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private sealed class SyncProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
