using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Transfers;

namespace Portal.ViewModels;

/// <summary>One row of the transfer list.</summary>
public sealed partial class TransferItemViewModel(TransferItem item, TransfersViewModel owner) : ObservableObject
{
    /// <summary>The engine's item.</summary>
    public TransferItem Item { get; } = item;

    /// <summary>Name at the destination.</summary>
    public string Name => Item.DestinationName;

    /// <summary>Upload or download arrow.</summary>
    public string Glyph => Item.Direction == TransferDirection.Upload ? Glyphs.Upload : Glyphs.Download;

    /// <summary>True for uploads.</summary>
    public bool IsUpload => Item.Direction == TransferDirection.Upload;

    /// <summary>"C:\Users\…\Downloads → Tower /mnt/user/media".</summary>
    public string Route => $"{Item.Source.FullPath} → {Item.Batch.DestinationLabel} {Item.Batch.DestinationDirectory}";

    /// <summary>Progress, 0 to 100.</summary>
    public double Percent => Item.Fraction * 100;

    /// <summary>Current state.</summary>
    public TransferStatus Status => Item.Status;

    /// <summary>"48 MB/s · 2 min left", "Waiting", "Done", or the error.</summary>
    public string StatusText => Item.Status switch
    {
        TransferStatus.Running when Item.TotalBytes == 0 && Item.FilesTotal == 0 => "Counting files…",
        TransferStatus.Running => $"{Formatting.Size((long)Item.BytesPerSecond)}/s · {Formatting.TimeLeft(Item.BytesPerSecond > 0 ? (Item.TotalBytes - Item.BytesDone) / Item.BytesPerSecond : double.NaN)}".TrimEnd(' ', '·'),
        TransferStatus.Queued => Item.Resuming ? $"Resuming from {Formatting.Size(Item.BytesDone)}" : "Waiting",
        TransferStatus.Paused => $"Paused at {Math.Round(Percent)} %",
        TransferStatus.Done when Item.SkippedLinks.Count > 0 => $"Done · skipped {Formatting.Count(Item.SkippedLinks.Count, "symbolic link")}: {string.Join(", ", Item.SkippedLinks.Take(3))}{(Item.SkippedLinks.Count > 3 ? $" and {Item.SkippedLinks.Count - 3} more" : "")}",
        TransferStatus.Done => "Done",
        TransferStatus.Cancelled => "Cancelled",
        _ => Item.Error ?? "Failed",
    };

    /// <summary>"1.2 GB of 6.1 GB" or "12 of 40 files".</summary>
    public string AmountText => Item.FilesTotal > 1
        ? $"{Item.FilesDone} of {Item.FilesTotal} files · {Formatting.Size(Item.BytesDone)} of {Formatting.Size(Item.TotalBytes)}"
        : Item.TotalBytes > 0 ? $"{Formatting.Size(Item.BytesDone)} of {Formatting.Size(Item.TotalBytes)}" : "";

    /// <summary>True while running.</summary>
    public bool CanPause => Item.Status is TransferStatus.Running or TransferStatus.Queued;

    /// <summary>True while paused.</summary>
    public bool CanResume => Item.Status == TransferStatus.Paused;

    /// <summary>True after a failure.</summary>
    public bool CanRetry => Item.Status == TransferStatus.Failed;

    /// <summary>True until finished.</summary>
    public bool CanCancel => Item.Status is TransferStatus.Running or TransferStatus.Queued or TransferStatus.Paused or TransferStatus.Failed;

    /// <summary>True once finished.</summary>
    public bool CanShow => Item.Status == TransferStatus.Done;

    /// <summary>Refreshes every bound property from the engine item.</summary>
    public void Update() => OnPropertyChanged(string.Empty);

    [RelayCommand]
    private void Pause() => owner.Engine.Pause(Item);

    [RelayCommand]
    private void Resume() => owner.Engine.Resume(Item);

    [RelayCommand]
    private Task Retry() => owner.RetryAsync(Item);

    [RelayCommand]
    private void Cancel() => owner.Engine.Cancel(Item);

    [RelayCommand]
    private void Show() => owner.ShowItem?.Invoke(Item);
}

/// <summary>The transfer list and its summary in the status bar.</summary>
public sealed partial class TransfersViewModel : ObservableObject
{
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<Guid, TransferItemViewModel> _rows = [];

    /// <summary>Creates the list over an engine.</summary>
    public TransfersViewModel(TransferEngine engine)
    {
        Engine = engine;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Tick(), Application.Current.Dispatcher);
        engine.Changed += () => Application.Current.Dispatcher.InvokeAsync(Sync);
        _timer.Start();
    }

    /// <summary>The engine doing the work.</summary>
    public TransferEngine Engine { get; }

    /// <summary>Rows, newest first.</summary>
    public ObservableCollection<TransferItemViewModel> Items { get; } = [];

    /// <summary>Reconnects before a retry when the connection dropped.</summary>
    public Func<Task>? EnsureConnected { get; set; }

    /// <summary>Opens the destination folder of a finished item.</summary>
    public Action<TransferItem>? ShowItem { get; set; }

    [ObservableProperty]
    private bool _isOpen;

    /// <summary>Number of queued and running items.</summary>
    public int ActiveCount => Items.Count(i => i.Status is TransferStatus.Running or TransferStatus.Queued);

    /// <summary>True when any item failed.</summary>
    public bool HasFailures => Items.Any(i => i.Status == TransferStatus.Failed);

    /// <summary>True when the list holds finished rows.</summary>
    public bool HasFinished => Items.Any(i => i.Status is TransferStatus.Done or TransferStatus.Cancelled);

    /// <summary>True when the list is empty.</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>Overall progress of active items, 0 to 100.</summary>
    public double OverallPercent
    {
        get
        {
            var active = Items.Where(i => i.Status is TransferStatus.Running or TransferStatus.Queued).Select(i => i.Item).ToList();
            var total = active.Sum(i => i.TotalBytes);
            return total == 0 ? 0 : active.Sum(i => i.BytesDone) * 100.0 / total;
        }
    }

    /// <summary>Status bar text such as "2 transfers · 3 min left".</summary>
    public string Summary
    {
        get
        {
            var active = Items.Where(i => i.Status is TransferStatus.Running or TransferStatus.Queued).Select(i => i.Item).ToList();
            if (active.Count == 0)
            {
                var failed = Items.Count(i => i.Status == TransferStatus.Failed);
                return failed > 0 ? $"{Formatting.Count(failed, "transfer")} failed" : "Transfers";
            }
            var rate = active.Sum(i => i.BytesPerSecond);
            var left = active.Sum(i => i.TotalBytes - i.BytesDone);
            var eta = rate > 0 ? " · " + Formatting.TimeLeft(left / rate) : "";
            return Formatting.Count(active.Count, "transfer") + eta;
        }
    }

    /// <summary>Header text inside the transfer panel.</summary>
    public string PanelSummary => ActiveCount > 0 ? Summary : HasFailures ? Summary : Items.Count > 0 ? "All done" : "";

    internal async Task RetryAsync(TransferItem item)
    {
        try
        {
            if (EnsureConnected is { } ensure) await ensure();
            Engine.Resume(item);
        }
        catch (Exception e)
        {
            ReportError?.Invoke("Couldn’t retry the transfer", Portal.Core.Connections.FriendlyError.ForOperation(e));
        }
    }

    /// <summary>Shows an error to the user.</summary>
    public Action<string, string>? ReportError { get; set; }

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    [RelayCommand]
    private void ClearFinished() => Engine.ClearFinished();

    [RelayCommand]
    private void PauseAll() => Engine.PauseAll();

    private void Sync()
    {
        var snapshot = Engine.Snapshot();
        var ids = snapshot.Select(i => i.Id).ToHashSet();
        foreach (var gone in _rows.Keys.Where(k => !ids.Contains(k)).ToList())
        {
            Items.Remove(_rows[gone]);
            _rows.Remove(gone);
        }
        foreach (var item in snapshot)
        {
            if (_rows.ContainsKey(item.Id)) continue;
            var row = new TransferItemViewModel(item, this);
            _rows[item.Id] = row;
            Items.Insert(0, row);
        }
        Tick();
    }

    private void Tick()
    {
        foreach (var row in Items) row.Update();
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(HasFailures));
        OnPropertyChanged(nameof(HasFinished));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(OverallPercent));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(PanelSummary));
    }
}
