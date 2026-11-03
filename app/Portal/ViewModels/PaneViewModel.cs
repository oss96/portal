using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Connections;
using Portal.Core.Files;
using Portal.Services;

namespace Portal.ViewModels;

/// <summary>Columns a file list can be sorted by.</summary>
public enum SortKey
{
    /// <summary>Name, natural order.</summary>
    Name,
    /// <summary>Modification time.</summary>
    Modified,
    /// <summary>File size.</summary>
    Size,
    /// <summary>Permission bits.</summary>
    Permissions,
    /// <summary>Owner name.</summary>
    Owner,
}

/// <summary>One file list with its own folder, history, filter, sort and selection.</summary>
public sealed partial class PaneViewModel : ObservableObject
{
    private readonly ToastService _toasts;
    private readonly Func<bool> _showHidden;
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();
    private CancellationTokenSource? _loading;
    private bool _loadedOnce;

    /// <summary>Creates a pane over <paramref name="fileSystem"/>, opening <paramref name="startPath"/> once <see cref="StartAsync"/> runs.</summary>
    public PaneViewModel(IFileSystem fileSystem, string startPath, Func<bool> showHidden, ToastService toasts)
    {
        FileSystem = fileSystem;
        _path = startPath;
        _pathText = startPath;
        _showHidden = showHidden;
        _toasts = toasts;
        View = (ListCollectionView)CollectionViewSource.GetDefaultView(Items);
        View.Filter = o => o is EntryViewModel e && Visible(e);
        View.CustomSort = new EntryComparer(SortKey.Name, ListSortDirection.Ascending);
        OnPathChanged(startPath);
    }

    /// <summary>The file system this pane shows.</summary>
    public IFileSystem FileSystem { get; }

    /// <summary>True for the server pane.</summary>
    public bool IsRemote => FileSystem.IsRemote;

    /// <summary>"This PC" or the server name.</summary>
    public string Title => FileSystem.DisplayName;

    /// <summary>Glyph next to <see cref="Title"/>.</summary>
    public string TitleGlyph => IsRemote ? Glyphs.Server : Glyphs.Computer;

    /// <summary>Entries of the current folder.</summary>
    public ObservableCollection<EntryViewModel> Items { get; } = [];

    /// <summary>Filtered and sorted view the list binds to.</summary>
    public ListCollectionView View { get; }

    /// <summary>Breadcrumb trail for <see cref="Path"/>.</summary>
    public ObservableCollection<PathSegment> Breadcrumbs { get; } = [];

    /// <summary>Selected rows, kept in sync by the view.</summary>
    public IReadOnlyList<EntryViewModel> Selected { get; private set; } = [];

    /// <summary>Raised when the selection changes.</summary>
    public event Action? SelectionChanged;

    /// <summary>Asks the view to select rows by name and optionally start renaming the first.</summary>
    public event Action<IReadOnlyCollection<string>, bool>? SelectRequested;

    /// <summary>Asks the view to move keyboard focus into the list.</summary>
    public event Action? FocusRequested;

    [ObservableProperty]
    private string _path;

    [ObservableProperty]
    private string _pathText;

    [ObservableProperty]
    private bool _isEditingPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterPlaceholder))]
    private string _filter = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _loadError;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private SortKey _sortKey = SortKey.Name;

    [ObservableProperty]
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;

    /// <summary>Placeholder of the filter box, naming the folder.</summary>
    public string FilterPlaceholder => $"Filter {FileSystem.NameOf(Path)}";

    /// <summary>Last segment of the current path.</summary>
    public string FolderName => FileSystem.NameOf(Path);

    /// <summary>True when the list has nothing to show and is not loading.</summary>
    public bool ShowEmpty => !IsLoading && LoadError is null && View.Count == 0;

    /// <summary>Headline of the empty state.</summary>
    public string EmptyTitle => Filter.Length > 0 ? $"Nothing here matches “{Filter}”" : "This folder is empty";

    /// <summary>Hint under the empty-state headline.</summary>
    public string EmptyHint => Filter.Length > 0 ? "Clear the filter to see everything again." :
        IsRemote ? "Drag files here from This PC or Explorer to upload them." : "Drag files here from the server to download them.";

    /// <summary>"24 items · 3 selected (1.2 GB)".</summary>
    public string FooterText
    {
        get
        {
            var shown = View.Count;
            var text = Formatting.Count(shown, "item");
            if (Selected.Count > 0)
            {
                var bytes = Selected.Where(s => !s.IsDirectory).Sum(s => s.Entry.Size);
                text += $" · {Selected.Count} selected" + (bytes > 0 ? $" ({Formatting.Size(bytes)})" : "");
            }
            var hiddenByFilter = Filter.Length > 0 ? Items.Count(i => VisibleIgnoringFilter(i)) - shown : 0;
            if (hiddenByFilter > 0) text += $" · {hiddenByFilter} hidden by the filter";
            return text;
        }
    }

    /// <summary>True when there is history to go back to.</summary>
    public bool CanGoBack => _back.Count > 0;

    /// <summary>True when there is history to go forward to.</summary>
    public bool CanGoForward => _forward.Count > 0;

    /// <summary>True below the root.</summary>
    public bool CanGoUp => FileSystem.Parent(Path) is not null;

    /// <summary>Loads the start folder, falling back to the root when it is missing.</summary>
    public async Task StartAsync()
    {
        if (!await LoadAsync(Path, selectAfter: null)) await LoadAsync(FileSystem.RootPath, selectAfter: null);
    }

    /// <summary>Opens a folder, recording history.</summary>
    public async Task NavigateAsync(string path, IReadOnlyCollection<string>? selectAfter = null)
    {
        if (path == Path && _loadedOnce)
        {
            await RefreshAsync(selectAfter);
            return;
        }
        var previous = Path;
        if (await LoadAsync(path, selectAfter))
        {
            _back.Push(previous);
            _forward.Clear();
            NotifyHistory();
        }
    }

    /// <summary>Reloads the current folder, keeping or replacing the selection.</summary>
    public Task RefreshAsync(IReadOnlyCollection<string>? selectAfter = null) =>
        LoadAsync(Path, selectAfter ?? Selected.Select(s => s.Name).ToArray());

    /// <summary>Called by the view when the user changes the selection.</summary>
    public void SetSelection(IList items)
    {
        Selected = items.OfType<EntryViewModel>().ToArray();
        OnPropertyChanged(nameof(FooterText));
        SelectionChanged?.Invoke();
    }

    /// <summary>Selects rows by name, optionally starting an inline rename of the first one.</summary>
    public void RequestSelect(IReadOnlyCollection<string> names, bool rename = false) => SelectRequested?.Invoke(names, rename);

    /// <summary>Moves keyboard focus into the list.</summary>
    public void Focus() => FocusRequested?.Invoke();

    /// <summary>Re-applies the hidden-files setting.</summary>
    public void RefreshFilter()
    {
        View.Refresh();
        NotifyListState();
    }

    /// <summary>Sorts by a column; the same column again flips the direction.</summary>
    public void SortBy(SortKey key)
    {
        SortDirection = key == SortKey ? (SortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending) : ListSortDirection.Ascending;
        SortKey = key;
        View.CustomSort = new EntryComparer(SortKey, SortDirection);
    }

    /// <summary>Opens a folder entry; files are handled by the caller.</summary>
    public Task OpenAsync(EntryViewModel entry) => entry.IsDirectory ? NavigateAsync(entry.Path) : Task.CompletedTask;

    [RelayCommand]
    private async Task GoBack()
    {
        if (_back.Count == 0) return;
        var target = _back.Pop();
        var current = Path;
        if (await LoadAsync(target, selectAfter: [FileSystem.NameOf(current)])) _forward.Push(current);
        else _back.Push(target);
        NotifyHistory();
    }

    [RelayCommand]
    private async Task GoForward()
    {
        if (_forward.Count == 0) return;
        var target = _forward.Pop();
        var current = Path;
        if (await LoadAsync(target, selectAfter: null)) _back.Push(current);
        else _forward.Push(target);
        NotifyHistory();
    }

    [RelayCommand]
    private Task GoUp() => FileSystem.Parent(Path) is { } parent ? NavigateAsync(parent, [FileSystem.NameOf(Path)]) : Task.CompletedTask;

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    [RelayCommand]
    private Task GoTo(string path) => NavigateAsync(path);

    [RelayCommand]
    private void BeginEditPath()
    {
        PathText = FileSystem.IsRemote || Path.Length > 0 ? Path : FileSystem.DisplayName;
        IsEditingPath = true;
    }

    [RelayCommand]
    private void CancelEditPath() => IsEditingPath = false;

    [RelayCommand]
    private async Task CommitPath()
    {
        IsEditingPath = false;
        var target = FileSystem.ResolveTypedPath(PathText, Path);
        if (target is null) return;
        var entry = target.Length == 0 ? null : await TryStat(target);
        if (target.Length > 0 && entry is null)
        {
            _toasts.Show(ToastKind.Error, "Folder not found", $"“{PathText}” doesn’t exist on {FileSystem.DisplayName}.");
            return;
        }
        if (entry is { IsDirectory: false })
        {
            var parent = FileSystem.Parent(target) ?? FileSystem.RootPath;
            await NavigateAsync(parent, [entry.Name]);
            return;
        }
        await NavigateAsync(target);
    }

    [RelayCommand]
    private void ClearFilter() => Filter = "";

    partial void OnFilterChanged(string value)
    {
        View.Refresh();
        NotifyListState();
    }

    partial void OnPathChanged(string value)
    {
        Breadcrumbs.Clear();
        foreach (var s in FileSystem.Breadcrumbs(value)) Breadcrumbs.Add(s);
        OnPropertyChanged(nameof(FolderName));
        OnPropertyChanged(nameof(FilterPlaceholder));
        OnPropertyChanged(nameof(CanGoUp));
    }

    private async Task<FileEntry?> TryStat(string path)
    {
        try
        {
            return await FileSystem.StatAsync(path, CancellationToken.None);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<bool> LoadAsync(string path, IReadOnlyCollection<string>? selectAfter)
    {
        _loading?.Cancel();
        var cts = _loading = new CancellationTokenSource();
        _ = Task.Delay(150, CancellationToken.None).ContinueWith(_ => { if (!cts.IsCancellationRequested) IsLoading = true; }, TaskScheduler.FromCurrentSynchronizationContext());
        try
        {
            var entries = await FileSystem.ListAsync(path, cts.Token);
            if (cts.IsCancellationRequested) return false;
            var leftFolder = path != Path;
            Items.Clear();
            foreach (var e in entries) Items.Add(new EntryViewModel(e));
            if (leftFolder) Filter = "";
            Path = path;
            LoadError = null;
            _loadedOnce = true;
            NotifyListState();
            if (selectAfter is { Count: > 0 }) RequestSelect(selectAfter);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception e)
        {
            var message = FriendlyError.ForOperation(e);
            if (!_loadedOnce)
            {
                Path = path;
                LoadError = $"Couldn’t open {FileSystem.NameOf(path)}. {message}";
            }
            else _toasts.Show(ToastKind.Error, $"Couldn’t open {FileSystem.NameOf(path)}", message);
            return false;
        }
        finally
        {
            if (_loading == cts)
            {
                cts.Cancel();
                IsLoading = false;
                OnPropertyChanged(nameof(ShowEmpty));
            }
        }
    }

    private void NotifyHistory()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
    }

    private void NotifyListState()
    {
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyHint));
        OnPropertyChanged(nameof(FooterText));
    }

    private bool VisibleIgnoringFilter(EntryViewModel e) => _showHidden() || !e.IsHidden;

    private bool Visible(EntryViewModel e) =>
        VisibleIgnoringFilter(e) && (Filter.Length == 0 || e.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase));

    private sealed class EntryComparer(SortKey key, ListSortDirection direction) : IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (x is not EntryViewModel a || y is not EntryViewModel b) return 0;
            var kind = a.KindOrder.CompareTo(b.KindOrder);
            if (kind != 0) return kind;
            var c = key switch
            {
                SortKey.Modified => a.Entry.ModifiedUtc.CompareTo(b.Entry.ModifiedUtc),
                SortKey.Size => a.Entry.Size.CompareTo(b.Entry.Size),
                SortKey.Permissions => (a.Entry.Mode ?? 0).CompareTo(b.Entry.Mode ?? 0),
                SortKey.Owner => string.Compare(a.Owner, b.Owner, StringComparison.OrdinalIgnoreCase),
                _ => 0,
            };
            if (c == 0) c = NaturalCompare(a.Name, b.Name);
            return direction == ListSortDirection.Ascending ? c : -c;
        }

        private static int NaturalCompare(string a, string b) =>
            string.Compare(a, b, System.Globalization.CultureInfo.CurrentCulture, System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.NumericOrdering);
    }
}
