using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Portal.Core;
using Portal.Core.Connections;
using Portal.Core.Ssh;

namespace Portal.ViewModels;

/// <summary>State of one connect step on a server card.</summary>
public enum StepState
{
    /// <summary>Not reached yet.</summary>
    Waiting,
    /// <summary>In progress.</summary>
    Active,
    /// <summary>Finished.</summary>
    Done,
}

/// <summary>One line of the connect progress.</summary>
public sealed partial class ConnectStepViewModel(string text) : ObservableObject
{
    /// <summary>What the step does.</summary>
    public string Text { get; } = text;

    [ObservableProperty]
    private StepState _state;
}

/// <summary>A saved server on the connect screen.</summary>
public sealed partial class ServerCardViewModel(ServerProfile profile, MainViewModel main) : ObservableObject
{
    private CancellationTokenSource? _connecting;

    /// <summary>The saved server.</summary>
    public ServerProfile Profile { get; } = profile;

    /// <summary>Display name.</summary>
    public string Title => Profile.Name.Length > 0 ? Profile.Name : Profile.Host;

    /// <summary>"root@tower · last used today".</summary>
    public string Subtitle
    {
        get
        {
            var when = Profile.LastConnectedUtc is { } t ? "last used " + LowerRelative(Formatting.Date(t)) : "not used yet";
            return main.IsOpen(Profile) ? $"{Profile.Address} · connected" : $"{Profile.Address} · {when}";
        }
    }

    private static string LowerRelative(string date) =>
        date.StartsWith("Today", StringComparison.Ordinal) || date.StartsWith("Yesterday", StringComparison.Ordinal) ? char.ToLowerInvariant(date[0]) + date[1..] : date;

    /// <summary>Connect progress lines.</summary>
    public ObservableCollection<ConnectStepViewModel> Steps { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorTitle;

    [ObservableProperty]
    private string? _errorDetail;

    /// <summary>True when an error is shown.</summary>
    public bool HasError => ErrorTitle is not null;

    /// <summary>True when the connect button is shown.</summary>
    public bool IsIdle => !IsBusy;

    /// <summary>"Open" when connected, otherwise "Connect".</summary>
    public string ConnectText => main.IsOpen(Profile) ? "Open" : "Connect";

    internal CancellationToken Begin()
    {
        _connecting?.Cancel();
        _connecting = new CancellationTokenSource();
        ErrorTitle = null;
        ErrorDetail = null;
        var target = SshConnector.Resolve(Profile);
        Steps.Clear();
        foreach (var text in new[] { $"Reaching {target.HostName}", "Checking the server’s identity", $"Signing in as {target.User}", "Opening files" })
            Steps.Add(new ConnectStepViewModel(text));
        SetStage(ConnectStage.Reaching);
        IsBusy = true;
        return _connecting.Token;
    }

    internal void SetStage(ConnectStage stage)
    {
        var index = (int)stage;
        for (var i = 0; i < Steps.Count; i++) Steps[i].State = i < index ? StepState.Done : i == index ? StepState.Active : StepState.Waiting;
    }

    internal void Fail(string title, string detail)
    {
        IsBusy = false;
        Steps.Clear();
        ErrorTitle = title;
        ErrorDetail = detail;
    }

    internal void Reset()
    {
        IsBusy = false;
        Steps.Clear();
        ErrorTitle = null;
        ErrorDetail = null;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(ConnectText));
    }

    [RelayCommand]
    private Task Connect() => main.ConnectAsync(this);

    [RelayCommand]
    private void Cancel()
    {
        _connecting?.Cancel();
        Reset();
    }

    [RelayCommand]
    private void Edit() => main.Connect.BeginEdit(Profile);

    [RelayCommand]
    private Task Remove() => main.RemoveServerAsync(this);
}

/// <summary>The connect screen: saved servers and the add/edit form.</summary>
public sealed partial class ConnectViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private string? _editingId;

    /// <summary>Creates the screen.</summary>
    public ConnectViewModel(MainViewModel main)
    {
        _main = main;
        foreach (var p in main.Servers) Cards.Add(new ServerCardViewModel(p, main));
        RefreshAliases();
    }

    /// <summary>Saved servers.</summary>
    public ObservableCollection<ServerCardViewModel> Cards { get; } = [];

    /// <summary>Hosts from ~/.ssh/config that are not saved yet.</summary>
    public ObservableCollection<string> ConfigAliases { get; } = [];

    /// <summary>True when there are no saved servers.</summary>
    public bool HasNoServers => Cards.Count == 0;

    /// <summary>True while connected, so a Back button can return to the browser.</summary>
    public bool CanGoBack => _main.Browser is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _host = "";

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _user = "";

    [ObservableProperty]
    private string _port = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AuthHint), nameof(UsesKeyFile))]
    private AuthKind _auth = AuthKind.DefaultKeys;

    [ObservableProperty]
    private string? _keyFile;

    [ObservableProperty]
    private string? _formError;

    /// <summary>"Add a server" or "Edit server".</summary>
    public string FormTitle => _editingId is null ? "Add a server" : "Edit server";

    /// <summary>"Save and connect" or "Save".</summary>
    public string SaveText => _editingId is null ? "Save and connect" : "Save changes";

    /// <summary>True when editing an existing server.</summary>
    public bool IsEditing => _editingId is not null;

    /// <summary>True once an address is entered.</summary>
    public bool CanSave => Host.Trim().Length > 0;

    /// <summary>True when the key-file picker is shown.</summary>
    public bool UsesKeyFile => Auth == AuthKind.KeyFile;

    /// <summary>Explains the chosen sign-in method.</summary>
    public string AuthHint => Auth switch
    {
        AuthKind.KeyFile => "Pick a private key file. Portal remembers its location, never the key.",
        AuthKind.Password => "Portal asks for the password each time and never stores it.",
        _ => $"Uses the keys in {AppPaths.SshDirectory} and your SSH config. Portal asks for a passphrase if a key has one.",
    };

    /// <summary>Fills the form with an existing server.</summary>
    public void BeginEdit(ServerProfile p)
    {
        _editingId = p.Id;
        Name = p.Name;
        Host = p.Host;
        User = p.User;
        Port = p.Port > 0 ? p.Port.ToString() : "";
        Auth = p.Auth;
        KeyFile = p.KeyFile;
        FormError = null;
        NotifyForm();
    }

    internal void AddCard(ServerProfile p) => Cards.Add(new ServerCardViewModel(p, _main));

    internal void RefreshCards()
    {
        foreach (var c in Cards) c.Reset();
        OnPropertyChanged(nameof(HasNoServers));
        OnPropertyChanged(nameof(CanGoBack));
        RefreshAliases();
    }

    [RelayCommand]
    private void UseAlias(string alias)
    {
        ClearForm();
        Host = alias;
        Name = alias;
    }

    [RelayCommand]
    private void ChooseKeyFile()
    {
        var dialog = new OpenFileDialog { Title = "Choose a private key", InitialDirectory = AppPaths.SshDirectory, Filter = "All files|*.*" };
        if (dialog.ShowDialog() == true) KeyFile = dialog.FileName;
    }

    [RelayCommand]
    private void SetAuth(AuthKind kind) => Auth = kind;

    [RelayCommand]
    private void CancelEdit() => ClearForm();

    [RelayCommand]
    private void GoBack() => _main.ShowBrowser();

    [RelayCommand]
    private async Task Save()
    {
        var host = Host.Trim();
        if (host.Length == 0) return;
        var port = 0;
        if (Port.Trim().Length > 0 && (!int.TryParse(Port.Trim(), out port) || port is < 1 or > 65535))
        {
            FormError = "The port is a number from 1 to 65535.";
            return;
        }
        if (Auth == AuthKind.KeyFile && string.IsNullOrWhiteSpace(KeyFile))
        {
            FormError = "Choose a key file, or pick another sign-in method.";
            return;
        }
        var existing = _editingId is null ? null : _main.Servers.FirstOrDefault(s => s.Id == _editingId);
        var keyFile = Auth == AuthKind.KeyFile ? KeyFile : null;
        if (existing is not null && _main.IsOpen(existing)
            && (existing.Host != host || existing.User != User.Trim() || existing.Port != port || existing.Auth != Auth || existing.KeyFile != keyFile))
        {
            FormError = $"Portal is connected to {existing.Name}. Disconnect first to change its address, user, port or sign-in; the name can change now.";
            return;
        }
        FormError = null;
        var profile = existing ?? new ServerProfile();
        profile.Name = Name.Trim().Length > 0 ? Name.Trim() : host;
        profile.Host = host;
        profile.User = User.Trim();
        profile.Port = port;
        profile.Auth = Auth;
        profile.KeyFile = Auth == AuthKind.KeyFile ? KeyFile : null;
        var isNew = existing is null;
        _main.SaveServer(profile, isNew);
        ClearForm();
        if (isNew && Cards.FirstOrDefault(c => c.Profile == profile) is { } card) await _main.ConnectAsync(card);
        else foreach (var c in Cards.Where(c => c.Profile == profile)) c.Reset();
    }

    private void ClearForm()
    {
        _editingId = null;
        Name = Host = User = Port = "";
        Auth = AuthKind.DefaultKeys;
        KeyFile = null;
        FormError = null;
        NotifyForm();
    }

    private void NotifyForm()
    {
        OnPropertyChanged(nameof(FormTitle));
        OnPropertyChanged(nameof(SaveText));
        OnPropertyChanged(nameof(IsEditing));
    }

    private void RefreshAliases()
    {
        ConfigAliases.Clear();
        var saved = _main.Servers.Select(s => s.Host).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var a in SshConfig.Load(Path.Combine(AppPaths.SshDirectory, "config")).HostAliases)
            if (!saved.Contains(a)) ConfigAliases.Add(a);
    }
}
