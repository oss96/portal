using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Portal.Services;

/// <summary>Kind of in-app notification.</summary>
public enum ToastKind
{
    /// <summary>Something happened.</summary>
    Info,
    /// <summary>Something finished well.</summary>
    Success,
    /// <summary>Something went wrong; stays until closed.</summary>
    Error,
}

/// <summary>One notification in the bottom-right corner.</summary>
public sealed partial class ToastViewModel(ToastService owner, ToastKind kind, string title, string? body, string? actionLabel, Action? action) : ObservableObject
{
    /// <summary>Kind, which picks the icon and colour.</summary>
    public ToastKind Kind { get; } = kind;

    /// <summary>One-line headline.</summary>
    public string Title { get; } = title;

    /// <summary>Optional detail line.</summary>
    public string? Body { get; } = body;

    /// <summary>Optional link text, such as "Retry".</summary>
    public string? ActionLabel { get; } = actionLabel;

    /// <summary>Icon glyph for the kind.</summary>
    public string Glyph => Kind switch { ToastKind.Success => Glyphs.CheckMark, ToastKind.Error => Glyphs.Error, _ => Glyphs.Info };

    /// <summary>Runs the action and closes the toast.</summary>
    [RelayCommand]
    private void Act()
    {
        owner.Close(this);
        action?.Invoke();
    }

    /// <summary>Closes the toast.</summary>
    [RelayCommand]
    private void Close() => owner.Close(this);
}

/// <summary>Shows short notifications that fade out on their own, except errors.</summary>
public sealed class ToastService
{
    /// <summary>Visible toasts, newest last.</summary>
    public ObservableCollection<ToastViewModel> Items { get; } = [];

    /// <summary>Shows a toast from any thread.</summary>
    public void Show(ToastKind kind, string title, string? body = null, string? actionLabel = null, Action? action = null)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.InvokeAsync(() => Show(kind, title, body, actionLabel, action));
            return;
        }
        var toast = new ToastViewModel(this, kind, title, body, actionLabel, action);
        Items.Add(toast);
        while (Items.Count > 4) Items.RemoveAt(0);
        if (kind == ToastKind.Error) return;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(actionLabel is null ? 5 : 8) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Close(toast);
        };
        timer.Start();
    }

    internal void Close(ToastViewModel toast) => Items.Remove(toast);
}
