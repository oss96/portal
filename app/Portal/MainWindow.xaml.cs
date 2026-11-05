using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Portal.Core.Settings;
using Portal.Services;
using Portal.ViewModels;

namespace Portal;

/// <summary>The only window: hosts the current screen plus the dialog, toast and command-search layers.</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _main;
    private readonly SettingsStore _store;
    private bool _closeConfirmed;

    /// <summary>Creates the window and restores its last size and position.</summary>
    public MainWindow(MainViewModel main, SettingsStore store)
    {
        _main = main;
        _store = store;
        InitializeComponent();
        DataContext = main;
        Restore(store.LoadWindow());
        main.PropertyChanged += OnMainChanged;
        main.Dialogs.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DialogService.Current) && main.Dialogs.Current is not null)
                Dispatcher.InvokeAsync(FocusDialog, DispatcherPriority.Input);
        };
        PreviewKeyDown += OnWindowKey;
    }

    /// <inheritdoc />
    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed) return;
        e.Cancel = true;
        if (!await _main.ConfirmCloseAsync()) return;
        _closeConfirmed = true;
        SavePlacement();
        if (_main.Browser is { } b)
        {
            b.Dispose();
            await b.Session.DisposeAsync();
        }
        _ = Dispatcher.BeginInvoke(Close);
    }

    private void Restore(WindowPlacement p)
    {
        Width = Math.Max(MinWidth, p.Width);
        Height = Math.Max(MinHeight, p.Height);
        if (!double.IsNaN(p.Left) && !double.IsNaN(p.Top) && p.Left > SystemParameters.VirtualScreenLeft - 100 && p.Top > SystemParameters.VirtualScreenTop - 100
            && p.Left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 && p.Top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = p.Left;
            Top = p.Top;
        }
        if (p.Maximized) WindowState = WindowState.Maximized;
    }

    private void SavePlacement()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _store.Save(new WindowPlacement { Left = bounds.Left, Top = bounds.Top, Width = bounds.Width, Height = bounds.Height, Maximized = WindowState == WindowState.Maximized });
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Palette) && _main.Palette is not null)
            Dispatcher.InvokeAsync(() => { PaletteInput.Focus(); Keyboard.Focus(PaletteInput); }, DispatcherPriority.Input);
    }

    private void FocusDialog()
    {
        DialogContent.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void OnWindowKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (_main.Dialogs.Current is { } d)
        {
            d.Dismiss();
            e.Handled = true;
        }
        else if (_main.Palette is { } p)
        {
            p.CloseCommand.Execute(null);
            e.Handled = true;
        }
        else if (_main.Transfers.IsOpen)
        {
            _main.Transfers.IsOpen = false;
            e.Handled = true;
        }
    }

    private void OnDialogBackdrop(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == sender && _main.Dialogs.Current is { DismissOnBackdrop: true } d) d.Dismiss();
    }

    private void OnDialogSurface(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void OnPaletteBackdrop(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == sender) _main.Palette?.CloseCommand.Execute(null);
    }

    private void OnPaletteKey(object sender, KeyEventArgs e)
    {
        if (_main.Palette is not { } p) return;
        switch (e.Key)
        {
            case Key.Down:
                p.Move(1);
                e.Handled = true;
                break;
            case Key.Up:
                p.Move(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                p.RunCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnPaletteClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: PaletteItem item }) _main.Palette?.RunCommand.Execute(item);
    }
}
