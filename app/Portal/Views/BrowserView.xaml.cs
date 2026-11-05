using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Portal.ViewModels;

namespace Portal.Views;

/// <summary>A positive count becomes Visible, zero becomes Collapsed.</summary>
public sealed class CountToVisibility : IValueConverter
{
    /// <summary>Shared instance.</summary>
    public static CountToVisibility Instance { get; } = new();

    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>The connected screen. Lays out panes and the details pane according to the view-model's toggles.</summary>
public partial class BrowserView : UserControl
{
    private BrowserViewModel? _vm;

    /// <summary>Creates the view.</summary>
    public BrowserView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnChanged;
            _vm = e.NewValue as BrowserViewModel;
            if (_vm is null) return;
            _vm.PropertyChanged += OnChanged;
            ApplyLayout();
        };
        PreviewKeyDown += OnKey;
        SizeChanged += (_, _) => ApplyLayout();
    }

    private const double SplitWithDetailsMinWidth = 1440;

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BrowserViewModel.ShowLocalPane) or nameof(BrowserViewModel.ShowDetails) or nameof(BrowserViewModel.ActivePane)) ApplyLayout();
    }

    private void ApplyLayout()
    {
        if (_vm is null) return;
        LocalColumn.Width = _vm.ShowLocalPane ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        LocalList.Visibility = _vm.ShowLocalPane ? Visibility.Visible : Visibility.Collapsed;
        LocalRail.Visibility = _vm.ShowLocalPane ? Visibility.Collapsed : Visibility.Visible;
        PaneSplitter.Visibility = LocalList.Visibility;
        var details = _vm.ShowDetails && (!_vm.ShowLocalPane || ActualWidth == 0 || ActualWidth >= SplitWithDetailsMinWidth);
        DetailsColumn.Width = details ? new GridLength(296) : new GridLength(0);
        DetailsPane.Visibility = details ? Visibility.Visible : Visibility.Collapsed;
        RemoteList.SetCompact(_vm.ShowLocalPane && details);
        _vm.Remote.IsActive = _vm.ActivePane == _vm.Remote;
        _vm.Local.IsActive = _vm.ActivePane == _vm.Local;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_vm is null || e.Key != Key.F || Keyboard.Modifiers != ModifierKeys.Control) return;
        (_vm.ActivePane == _vm.Local ? LocalList : RemoteList).FocusFilter();
        e.Handled = true;
    }

    private void OnAddServer(object sender, RoutedEventArgs e) => _vm?.Main.ShowConnect();

    private void OnRailDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;
        if (_vm is not null && e.Data.GetData(DragPayload.Format) is DragPayload { Pane.IsRemote: true }) _vm.ShowLocalPane = true;
    }
}
