using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Portal.ViewModels;

namespace Portal.Views;

/// <summary>Rows dragged out of a pane.</summary>
public sealed record DragPayload(PaneViewModel Pane, IReadOnlyList<EntryViewModel> Items)
{
    /// <summary>Clipboard format name for in-app drags.</summary>
    public const string Format = "Portal.Entries";
}

/// <summary>One pane: navigation bar, breadcrumbs, filter and the file grid with Explorer-style mouse and keyboard handling.</summary>
public partial class FileListView : UserControl
{
    private PaneViewModel? _pane;
    private Point _dragStart;
    private bool _dragArmed;
    private EntryViewModel? _pressedSelected;

    /// <summary>Creates the view.</summary>
    public FileListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>Hides the owner column to save space when both panes and the details pane are shown.</summary>
    public void SetCompact(bool compact)
    {
        if (_pane is { IsRemote: true }) OwnerColumn.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Moves focus to the filter box.</summary>
    public void FocusFilter()
    {
        FilterBox.Focus();
        FilterBox.SelectAll();
    }

    private static BrowserViewModel? Browser => (Application.Current.MainWindow?.DataContext as MainViewModel)?.Browser;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_pane is not null)
        {
            _pane.SelectRequested -= OnSelectRequested;
            _pane.FocusRequested -= FocusList;
            _pane.PropertyChanged -= OnPaneChanged;
        }
        _pane = e.NewValue as PaneViewModel;
        if (_pane is null) return;
        _pane.SelectRequested += OnSelectRequested;
        _pane.FocusRequested += FocusList;
        _pane.PropertyChanged += OnPaneChanged;
        PermissionsColumn.Visibility = _pane.IsRemote ? Visibility.Visible : Visibility.Collapsed;
        OwnerColumn.Visibility = _pane.IsRemote ? Visibility.Visible : Visibility.Collapsed;
        ActiveBar.Visibility = _pane.IsActive ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPaneChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaneViewModel.IsActive) && _pane is not null)
            ActiveBar.Visibility = _pane.IsActive && Browser is { ShowLocalPane: true } ? Visibility.Visible : Visibility.Collapsed;
        if (e.PropertyName == nameof(PaneViewModel.Path)) Dispatcher.InvokeAsync(SnapCrumbs, DispatcherPriority.Loaded);
    }

    private void OnSelectRequested(IReadOnlyCollection<string> names, bool rename)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_pane is null) return;
            Grid.SelectedItems.Clear();
            EntryViewModel? first = null;
            foreach (var item in _pane.View.OfType<EntryViewModel>())
            {
                if (!names.Contains(item.Name)) continue;
                Grid.SelectedItems.Add(item);
                first ??= item;
            }
            if (first is null) return;
            Grid.ScrollIntoView(first);
            if (rename)
            {
                first.EditName = first.Name;
                first.IsRenaming = true;
            }
            else FocusRow(first);
        }, DispatcherPriority.Background);
    }

    private void FocusList()
    {
        Dispatcher.InvokeAsync(() =>
        {
            var item = Grid.SelectedItem as EntryViewModel ?? _pane?.View.OfType<EntryViewModel>().FirstOrDefault();
            if (item is null) Grid.Focus();
            else FocusRow(item);
        }, DispatcherPriority.Background);
    }

    private void FocusRow(EntryViewModel item)
    {
        Grid.CurrentCell = new DataGridCellInfo(item, Grid.Columns[0]);
        if (Grid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row) row.Focus();
        else Grid.Focus();
    }

    private void OnAnyMouseDown(object sender, MouseButtonEventArgs e) => Activate();

    private void OnFocused(object sender, KeyboardFocusChangedEventArgs e) => Activate();

    private void Activate()
    {
        if (_pane is not null) Browser?.Activate(_pane);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => _pane?.SetSelection(Grid.SelectedItems);

    private void OnSorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (_pane is null || !Enum.TryParse<SortKey>(e.Column.SortMemberPath, out var key)) return;
        _pane.SortBy(key);
        foreach (var c in Grid.Columns) c.SortDirection = c == e.Column ? _pane.SortDirection : null;
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_pane is null || RowAt(e.OriginalSource) is not { Item: EntryViewModel item } || item.IsRenaming) return;
        _ = Browser?.OpenEntryAsync(_pane, item);
    }

    private void OnGridKey(object sender, KeyEventArgs e)
    {
        if (_pane is null || Browser is not { } b || e.OriginalSource is TextBox) return;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        switch (key)
        {
            case Key.Enter when ctrl:
                b.TransferCommand.Execute(null);
                break;
            case Key.Enter when Grid.SelectedItem is EntryViewModel item:
                _ = b.OpenEntryAsync(_pane, item);
                break;
            case Key.Back:
            case Key.Up when alt:
                _pane.GoUpCommand.Execute(null);
                break;
            case Key.Left when alt:
                _pane.GoBackCommand.Execute(null);
                break;
            case Key.Right when alt:
                _pane.GoForwardCommand.Execute(null);
                break;
            case Key.Delete:
                b.DeleteCommand.Execute(null);
                break;
            case Key.F2:
                b.RenameCommand.Execute(null);
                break;
            case Key.C when ctrl:
                b.CopyItemsCommand.Execute(null);
                break;
            case Key.X when ctrl:
                b.CutItemsCommand.Execute(null);
                break;
            case Key.V when ctrl:
                b.PasteCommand.Execute(null);
                break;
            case Key.Escape when Grid.SelectedItems.Count > 0:
                Grid.UnselectAll();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void OnGridMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pressedSelected = null;
        _dragArmed = false;
        if (FindAncestor<DataGridColumnHeader>(e.OriginalSource) is not null || FindAncestor<ScrollBar>(e.OriginalSource) is not null) return;
        var row = RowAt(e.OriginalSource);
        if (row is null)
        {
            Grid.UnselectAll();
            Grid.Focus();
            return;
        }
        if (row.Item is not EntryViewModel item || item.IsRenaming || e.OriginalSource is TextBox) return;
        _dragStart = e.GetPosition(Grid);
        _dragArmed = true;
        if (row.IsSelected && Keyboard.Modifiers == ModifierKeys.None && Grid.SelectedItems.Count > 1)
        {
            _pressedSelected = item;
            row.Focus();
            e.Handled = true;
        }
    }

    private void OnGridMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_pressedSelected is { } item)
        {
            Grid.SelectedItems.Clear();
            Grid.SelectedItems.Add(item);
        }
        _pressedSelected = null;
        _dragArmed = false;
    }

    private void OnGridMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed || _pane is null) return;
        var delta = e.GetPosition(Grid) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragArmed = false;
        if (_pressedSelected is { } pressed && !Grid.SelectedItems.Contains(pressed)) Grid.SelectedItems.Add(pressed);
        _pressedSelected = null;
        var items = Grid.SelectedItems.OfType<EntryViewModel>().ToList();
        if (items.Count == 0) return;
        var data = new DataObject();
        data.SetData(DragPayload.Format, new DragPayload(_pane, items));
        if (!_pane.IsRemote)
        {
            var paths = new StringCollection();
            paths.AddRange([.. items.Select(i => i.Path)]);
            data.SetFileDropList(paths);
        }
        DragDrop.DoDragDrop(Grid, data, DragDropEffects.Copy | DragDropEffects.Move);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var (effect, text) = Evaluate(e);
        e.Effects = effect;
        DropOverlay.Visibility = effect == DragDropEffects.None ? Visibility.Collapsed : Visibility.Visible;
        DropText.Text = text;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        var p = e.GetPosition(this);
        if (p.X <= 0 || p.Y <= 0 || p.X >= ActualWidth || p.Y >= ActualHeight) DropOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (_pane is null || Browser is not { } b) return;
        var (effect, _) = Evaluate(e);
        if (effect == DragDropEffects.None) return;
        var folder = RowAt(e.OriginalSource)?.Item as EntryViewModel;
        if (e.Data.GetData(DragPayload.Format) is DragPayload payload) _ = b.DropEntriesAsync(payload.Pane, payload.Items, _pane, folder is { IsDirectory: true } ? folder : null);
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] files) _ = b.DropFilesAsync(files, _pane, folder is { IsDirectory: true } ? folder : null);
        e.Handled = true;
    }

    private (DragDropEffects, string) Evaluate(DragEventArgs e)
    {
        if (_pane is null) return (DragDropEffects.None, "");
        var folder = RowAt(e.OriginalSource)?.Item as EntryViewModel;
        if (folder is { IsDirectory: false }) folder = null;
        var where = folder?.Name ?? _pane.FolderName;
        if (e.Data.GetData(DragPayload.Format) is DragPayload payload)
        {
            if (payload.Pane == _pane)
                return _pane.IsRemote && folder is not null && !payload.Items.Contains(folder) ? (DragDropEffects.Move, $"Move to {where}") : (DragDropEffects.None, "");
            return (DragDropEffects.Copy, $"{(_pane.IsRemote ? "Upload to" : "Download to")} {where}");
        }
        if (_pane.IsRemote && e.Data.GetDataPresent(DataFormats.FileDrop)) return (DragDropEffects.Copy, $"Upload to {where}");
        return (DragDropEffects.None, "");
    }

    private void OnRenameVisible(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox box || e.NewValue is not true) return;
        box.Dispatcher.InvokeAsync(() =>
        {
            box.Focus();
            var dot = box.Text.LastIndexOf('.');
            var isFolder = box.DataContext is EntryViewModel { IsDirectory: true };
            box.Select(0, dot > 0 && !isFolder ? dot : box.Text.Length);
        }, DispatcherPriority.Input);
    }

    private void OnRenameKey(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: EntryViewModel item }) return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Commit(item);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            item.IsRenaming = false;
            item.EditName = item.Name;
            FocusRow(item);
        }
    }

    private void OnRenameLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: EntryViewModel { IsRenaming: true } item }) Commit(item);
    }

    private void Commit(EntryViewModel item)
    {
        if (_pane is null || Browser is not { } b || !item.IsRenaming) return;
        _ = b.CommitRenameAsync(_pane, item, item.EditName);
        FocusList();
    }

    private void OnCrumbsSized(object sender, SizeChangedEventArgs e) => SnapCrumbs();

    private void SnapCrumbs()
    {
        var containers = Enumerable.Range(0, CrumbItems.Items.Count).Select(i => CrumbItems.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement).ToList();
        if (containers.Count == 0 || containers.Any(c => c is null)) return;
        var widths = new List<double>();
        foreach (var c in containers)
        {
            c!.Visibility = Visibility.Visible;
            c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            widths.Add(c.DesiredSize.Width);
        }
        var available = CrumbScroll.ActualWidth - 8;
        var total = widths.Sum();
        var hidden = 0;
        while (total + (hidden > 0 ? 24 : 0) > available && hidden < containers.Count - 1) total -= widths[hidden++];
        for (var i = 0; i < hidden; i++) containers[i]!.Visibility = Visibility.Collapsed;
        CrumbEllipsis.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnCrumbsClick(object sender, MouseButtonEventArgs e)
    {
        if (_pane is null || _pane.IsEditingPath || FindAncestor<Button>(e.OriginalSource) is not null) return;
        _pane.BeginEditPathCommand.Execute(null);
    }

    private void OnPathVisible(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) PathBox.Dispatcher.InvokeAsync(() => { PathBox.Focus(); PathBox.SelectAll(); }, DispatcherPriority.Input);
    }

    private void OnPathKey(object sender, KeyEventArgs e)
    {
        if (_pane is null) return;
        if (e.Key == Key.Enter)
        {
            _pane.CommitPathCommand.Execute(null);
            FocusList();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _pane.CancelEditPathCommand.Execute(null);
            FocusList();
            e.Handled = true;
        }
    }

    private void OnPathLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_pane is { IsEditingPath: true }) _pane.CancelEditPathCommand.Execute(null);
    }

    private void OnFilterKey(object sender, KeyEventArgs e)
    {
        if (_pane is null) return;
        if (e.Key == Key.Escape)
        {
            _pane.Filter = "";
            FocusList();
            e.Handled = true;
        }
        else if (e.Key is Key.Down or Key.Enter)
        {
            FocusList();
            e.Handled = true;
        }
    }

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (_pane is null || Browser is not { } b || Grid.ContextMenu is not { } menu)
        {
            e.Handled = true;
            return;
        }
        if (FindAncestor<DataGridColumnHeader>(e.OriginalSource) is not null)
        {
            e.Handled = true;
            return;
        }
        b.Activate(_pane);
        var row = RowAt(e.OriginalSource);
        if (row is { Item: EntryViewModel clicked } && !row.IsSelected)
        {
            Grid.SelectedItems.Clear();
            Grid.SelectedItems.Add(clicked);
        }
        else if (row is null) Grid.UnselectAll();
        _pane.SetSelection(Grid.SelectedItems);
        menu.Items.Clear();
        var remote = _pane.IsRemote;
        if (row?.Item is EntryViewModel item)
        {
            var one = _pane.Selected.Count == 1;
            Add(menu, item.IsDirectory ? "Open" : remote ? "Open (edit on the server)" : "Open", Glyphs.OpenWith, "Enter", new AsyncAction(() => b.OpenEntryAsync(_pane, item)), one);
            Add(menu, b.TransferText, b.TransferGlyph, "Ctrl+Enter", b.TransferCommand);
            menu.Items.Add(new Separator());
            Add(menu, "Copy", Glyphs.Copy, "Ctrl+C", b.CopyItemsCommand);
            if (remote) Add(menu, "Cut", Glyphs.Cut, "Ctrl+X", b.CutItemsCommand);
            Add(menu, "Rename", Glyphs.Rename, "F2", b.RenameCommand);
            if (remote) Add(menu, "Permissions and owner…", Glyphs.Lock, "", b.EditPermissionsCommand);
            if (remote && one && item.IsDirectory) Add(menu, "Pin to sidebar", Glyphs.Pin, "", b.PinFolderCommand);
            Add(menu, "Copy path", Glyphs.Copy, "", b.CopyPathCommand);
            menu.Items.Add(new Separator());
            var delete = Add(menu, remote ? "Delete…" : "Delete", Glyphs.Delete, "Del", b.DeleteCommand);
            delete.Foreground = (Brush)FindResource("SystemFillColorCriticalBrush");
        }
        else
        {
            Add(menu, "New folder", Glyphs.NewFolder, "Ctrl+Shift+N", b.NewFolderCommand);
            Add(menu, "Paste", Glyphs.Paste, "Ctrl+V", b.PasteCommand);
            Add(menu, "Refresh", Glyphs.Refresh, "F5", b.RefreshCommand);
            Add(menu, b.ShowHidden ? "Hide hidden files" : "Show hidden files", Glyphs.View, "Ctrl+H", b.ToggleHiddenCommand);
            menu.Items.Add(new Separator());
            if (remote)
            {
                Add(menu, "Open terminal here", Glyphs.Terminal, "", b.OpenTerminalCommand);
                Add(menu, "Pin this folder to the sidebar", Glyphs.Pin, "", b.PinFolderCommand);
            }
            Add(menu, "Copy path", Glyphs.Copy, "", b.CopyPathCommand);
        }
    }

    private static MenuItem Add(ContextMenu menu, string header, string glyph, string gesture, ICommand command, bool enabled = true)
    {
        var item = new MenuItem
        {
            Header = header,
            Command = command,
            InputGestureText = gesture,
            Icon = new TextBlock { Text = glyph, FontFamily = (FontFamily)Application.Current.FindResource("IconFont"), FontSize = 14 },
        };
        if (!enabled) item.IsEnabled = false;
        menu.Items.Add(item);
        return item;
    }

    private DataGridRow? RowAt(object source) => FindAncestor<DataGridRow>(source);

    private static T? FindAncestor<T>(object source) where T : DependencyObject
    {
        var d = source as DependencyObject;
        while (d is not null and not T) d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    private sealed class AsyncAction(Func<Task> run) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _ = run();
    }
}
