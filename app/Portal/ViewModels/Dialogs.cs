using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Connections;
using Portal.Core.Files;
using Portal.Core.Transfers;
using Portal.Services;

namespace Portal.ViewModels;

/// <summary>Confirms a delete, naming the server and what goes.</summary>
public sealed partial class DeleteDialogViewModel : DialogViewModel<bool>
{
    /// <summary>Builds the confirmation for entries in <paramref name="pane"/>.</summary>
    public DeleteDialogViewModel(PaneViewModel pane, IReadOnlyList<EntryViewModel> entries) : base(false)
    {
        IsRemote = pane.IsRemote;
        Entries = entries;
        var what = Formatting.Count(entries.Count, "item");
        Title = IsRemote ? $"Delete {what} from {pane.Title}?" : $"Move {what} to the Recycle Bin?";
        Message = IsRemote
            ? $"They are removed from {pane.Path} for good{(entries.Any(e => e.IsDirectory) ? ", including everything inside the folders" : "")}. The server has no recycle bin, so this can’t be undone."
            : "You can restore them from the Recycle Bin.";
        ConfirmText = IsRemote ? "Delete" : "Move to Recycle Bin";
    }

    /// <summary>True when deleting on a server.</summary>
    public bool IsRemote { get; }

    /// <summary>What is deleted.</summary>
    public IReadOnlyList<EntryViewModel> Entries { get; }

    /// <summary>Headline.</summary>
    public string Title { get; }

    /// <summary>Consequence in plain words.</summary>
    public string Message { get; }

    /// <summary>Label of the confirm button.</summary>
    public string ConfirmText { get; }

    [RelayCommand]
    private void Confirm() => Close(true);

    [RelayCommand]
    private void Cancel() => Close(false);
}

/// <summary>Asks what to do when a transferred item already exists.</summary>
public sealed partial class ConflictDialogViewModel(ConflictQuestion question) : DialogViewModel<ConflictAnswer>(new ConflictAnswer(ConflictChoice.Cancel, false))
{
    [ObservableProperty]
    private bool _applyToRest;

    /// <summary>Headline.</summary>
    public string Title => $"“{question.Incoming.Name}” is already on {question.DestinationLabel}";

    /// <summary>Label of the incoming copy.</summary>
    public string IncomingLabel => question.Direction == TransferDirection.Upload ? "Yours on This PC" : "On the server";

    /// <summary>Size and date of the incoming copy.</summary>
    public string IncomingDetail => Describe(question.Incoming);

    /// <summary>Label of the existing copy.</summary>
    public string ExistingLabel => $"Already on {question.DestinationLabel}";

    /// <summary>Size and date of the existing copy.</summary>
    public string ExistingDetail => Describe(question.Existing);

    /// <summary>Explains Replace for files and folders.</summary>
    public string ReplaceHint => question.Incoming.IsDirectory
        ? "Files with the same name inside are replaced; other files stay."
        : $"The copy on {question.DestinationLabel} is overwritten.";

    /// <summary>Label of the Replace choice.</summary>
    public string ReplaceText => question.Incoming.IsDirectory ? "Merge into it" : "Replace it";

    /// <summary>Explains Keep both.</summary>
    public string KeepBothHint => $"The new copy is saved as “{question.KeepBothName}”.";

    /// <summary>Explains Skip.</summary>
    public string SkipHint => $"Nothing changes on {question.DestinationLabel}.";

    /// <summary>True when more conflicts follow.</summary>
    public bool HasMore => question.Remaining > 0;

    /// <summary>"Do the same for the next 3 conflicts".</summary>
    public string ApplyToRestText => $"Do the same for the next {Formatting.Count(question.Remaining, "conflict")}";

    /// <inheritdoc />
    public override bool DismissOnBackdrop => false;

    [RelayCommand]
    private void Choose(ConflictChoice choice) => Close(new ConflictAnswer(choice, ApplyToRest));

    private static string Describe(FileEntry e) =>
        (e.IsDirectory ? "Folder" : Formatting.Size(e.Size)) + (e.ModifiedUtc > DateTime.UnixEpoch ? " · " + Formatting.Date(e.ModifiedUtc) : "");
}

/// <summary>What the permissions dialog changes.</summary>
public sealed record PermissionChange(int Mode, string? Owner, string? Group, bool Recursive);

/// <summary>One checkbox cell of the permission grid.</summary>
public sealed partial class PermissionBit(PermissionsDialogViewModel owner, int bit, string label) : ObservableObject
{
    /// <summary>Accessible name such as "Group can write".</summary>
    public string Label { get; } = label;

    /// <summary>Whether the bit is set.</summary>
    public bool IsSet
    {
        get => (owner.Mode & bit) != 0;
        set
        {
            owner.Mode = value ? owner.Mode | bit : owner.Mode & ~bit;
            OnPropertyChanged();
        }
    }

    internal void Refresh() => OnPropertyChanged(nameof(IsSet));
}

/// <summary>One row (owner, group, everyone) of the permission grid.</summary>
public sealed record PermissionRow(string Who, PermissionBit Read, PermissionBit Write, PermissionBit Run);

/// <summary>Edits mode bits and ownership of server entries.</summary>
public sealed partial class PermissionsDialogViewModel : DialogViewModel<PermissionChange?>
{
    private readonly int _originalMode;
    private readonly string? _originalOwner;
    private readonly string? _originalGroup;

    /// <summary>Starts from the first entry's current values.</summary>
    public PermissionsDialogViewModel(IReadOnlyList<EntryViewModel> entries, AccountNames accounts) : base(null)
    {
        var first = entries[0].Entry;
        _originalMode = first.Mode ?? 0x1A4;
        _mode = _originalMode;
        _originalOwner = first.Owner;
        _originalGroup = first.Group;
        _owner = first.Owner;
        _group = first.Group;
        _octalText = Permissions.ToOctal(_mode);
        Title = entries.Count == 1 ? $"Permissions for “{first.Name}”" : $"Permissions for {entries.Count} items";
        HasFolders = entries.Any(e => e.IsDirectory);
        Owners = [.. accounts.UserNames.Union(first.Owner is null ? [] : [first.Owner]).Order(StringComparer.Ordinal)];
        Groups = [.. accounts.GroupNames.Union(first.Group is null ? [] : [first.Group]).Order(StringComparer.Ordinal)];
        string[] who = ["Owner", "Group", "Everyone"];
        Rows = [.. who.Select((w, i) => new PermissionRow(w,
            new PermissionBit(this, 0x100 >> (i * 3), $"{w} can read"),
            new PermissionBit(this, 0x80 >> (i * 3), $"{w} can write"),
            new PermissionBit(this, 0x40 >> (i * 3), $"{w} can run")))];
    }

    /// <summary>Headline.</summary>
    public string Title { get; }

    /// <summary>True when a folder is selected, enabling the recursive option.</summary>
    public bool HasFolders { get; }

    /// <summary>The 3×3 grid.</summary>
    public IReadOnlyList<PermissionRow> Rows { get; }

    /// <summary>Users on the server.</summary>
    public IReadOnlyList<string> Owners { get; }

    /// <summary>Groups on the server.</summary>
    public IReadOnlyList<string> Groups { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SymbolicText))]
    private int _mode;

    [ObservableProperty]
    private string _octalText;

    [ObservableProperty]
    private string? _owner;

    [ObservableProperty]
    private string? _group;

    [ObservableProperty]
    private bool _recursive;

    /// <summary>"rwxr-xr-x".</summary>
    public string SymbolicText => Permissions.ToSymbolic(Mode);

    partial void OnModeChanged(int value)
    {
        if (!Permissions.TryParseOctal(OctalText, out var typed) || typed != value) OctalText = Permissions.ToOctal(value);
        foreach (var r in Rows ?? [])
        {
            r.Read.Refresh();
            r.Write.Refresh();
            r.Run.Refresh();
        }
    }

    partial void OnOctalTextChanged(string value)
    {
        if (Permissions.TryParseOctal(value, out var m) && m != Mode) Mode = m;
    }

    [RelayCommand]
    private void Apply()
    {
        var owner = Owner != _originalOwner ? Owner : null;
        var group = Group != _originalGroup ? Group : null;
        Close(new PermissionChange(Mode, owner, group, Recursive));
    }

    [RelayCommand]
    private void Cancel() => Close(null);

    /// <summary>True when the dialog would change anything.</summary>
    public bool Changes => Mode != _originalMode || Owner != _originalOwner || Group != _originalGroup;
}

/// <summary>Asks whether to trust a server seen for the first time.</summary>
public sealed partial class HostKeyDialogViewModel(HostKeyInfo key) : DialogViewModel<bool>(false)
{
    /// <summary>Headline.</summary>
    public string Title => $"Is this really {key.Host}?";

    /// <summary>The fingerprint to compare.</summary>
    public string Fingerprint => $"{key.KeyType} · {key.Fingerprint}";

    /// <summary>Where Portal saves the key.</summary>
    public string SaveNote => $"Portal saves it to {key.KnownHostsPath} and refuses to connect if it ever changes.";

    /// <inheritdoc />
    public override bool DismissOnBackdrop => false;

    [RelayCommand]
    private void Trust() => Close(true);

    [RelayCommand]
    private void Cancel() => Close(false);
}

/// <summary>Asks for a key passphrase or an account password.</summary>
public sealed partial class SecretDialogViewModel(string title, string message, string label, bool retry) : DialogViewModel<string?>(null)
{
    /// <summary>Headline.</summary>
    public string Title { get; } = title;

    /// <summary>Explanation.</summary>
    public string Message { get; } = message;

    /// <summary>Field label.</summary>
    public string Label { get; } = label;

    /// <summary>True after a wrong attempt.</summary>
    public bool IsRetry { get; } = retry;

    /// <summary>Set by the view's password box before <see cref="SubmitCommand"/> runs.</summary>
    public string Secret { get; set; } = "";

    [RelayCommand]
    private void Submit() => Close(Secret);

    [RelayCommand]
    private void Cancel() => Close(null);
}

/// <summary>A short message with one button.</summary>
public sealed partial class MessageDialogViewModel(string title, string message, string confirmText = "OK", string? cancelText = null) : DialogViewModel<bool>(false)
{
    /// <summary>Headline.</summary>
    public string Title { get; } = title;

    /// <summary>Body text.</summary>
    public string Message { get; } = message;

    /// <summary>Label of the main button.</summary>
    public string ConfirmText { get; } = confirmText;

    /// <summary>Label of the secondary button, or null for none.</summary>
    public string? CancelText { get; } = cancelText;

    [RelayCommand]
    private void Confirm() => Close(true);

    [RelayCommand]
    private void Cancel() => Close(false);
}
