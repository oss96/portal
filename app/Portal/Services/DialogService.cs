using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Portal.Services;

/// <summary>Base for in-window dialogs shown over a dimmed backdrop.</summary>
public abstract class DialogViewModel : ObservableObject
{
    internal event Action? Closed;

    /// <summary>Closes the dialog the way Escape does.</summary>
    public abstract void Dismiss();

    /// <summary>False for dialogs that must be answered with a button.</summary>
    public virtual bool DismissOnBackdrop => true;

    /// <summary>Notifies the host that the dialog is done.</summary>
    protected void RaiseClosed() => Closed?.Invoke();
}

/// <summary>A dialog that produces a <typeparamref name="T"/> when it closes.</summary>
public abstract class DialogViewModel<T>(T dismissResult) : DialogViewModel
{
    private readonly TaskCompletionSource<T> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the dialog closes.</summary>
    public Task<T> Result => _result.Task;

    /// <summary>Closes with <paramref name="value"/>.</summary>
    public void Close(T value)
    {
        if (_result.TrySetResult(value)) RaiseClosed();
    }

    /// <inheritdoc />
    public override void Dismiss() => Close(dismissResult);
}

/// <summary>Shows one dialog at a time; later requests wait their turn.</summary>
public sealed partial class DialogService : ObservableObject
{
    private readonly Queue<DialogViewModel> _waiting = new();

    [ObservableProperty]
    private DialogViewModel? _current;

    /// <summary>Shows a dialog from any thread and waits for its result.</summary>
    public Task<T> ShowAsync<T>(DialogViewModel<T> dialog)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (!dispatcher.CheckAccess()) return dispatcher.InvokeAsync(() => ShowAsync(dialog)).Task.Unwrap();
        dialog.Closed += () => Advance(dialog);
        if (Current is null) Current = dialog;
        else _waiting.Enqueue(dialog);
        return dialog.Result;
    }

    private void Advance(DialogViewModel closed)
    {
        if (Current != closed) return;
        Current = _waiting.Count > 0 ? _waiting.Dequeue() : null;
    }
}
