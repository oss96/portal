using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Connections;
using Portal.Core.Files;
using Portal.Core.Settings;
using Portal.Core.Transfers;
using Portal.Services;

namespace Portal.ViewModels;

/// <summary>App-wide state: saved servers, preferences, the current screen, dialogs, toasts and transfers.</summary>
public sealed partial class MainViewModel : ObservableObject, IConnectPrompts
{
    private readonly SettingsStore _store;

    /// <summary>Loads settings and builds the connect screen.</summary>
    public MainViewModel(SettingsStore store)
    {
        _store = store;
        Preferences = store.LoadPreferences();
        foreach (var s in store.LoadServers()) Servers.Add(s);
        Transfers = new TransfersViewModel(new TransferEngine(() => Preferences.ParallelTransfers));
        Transfers.Engine.BatchFinished += OnBatchFinished;
        Transfers.ReportError = (title, body) => Toasts.Show(ToastKind.Error, title, body);
        Updates = new UpdatesViewModel(this);
        Connect = new ConnectViewModel(this);
        _current = Connect;
    }

    /// <summary>User preferences.</summary>
    public Preferences Preferences { get; }

    /// <summary>Saved servers.</summary>
    public ObservableCollection<ServerProfile> Servers { get; } = [];

    /// <summary>This PC's file system.</summary>
    public LocalFileSystem LocalFiles { get; } = new();

    /// <summary>In-window dialogs.</summary>
    public DialogService Dialogs { get; } = new();

    /// <summary>In-window notifications.</summary>
    public ToastService Toasts { get; } = new();

    /// <summary>Uploads and downloads.</summary>
    public TransfersViewModel Transfers { get; }

    /// <summary>Update checks and installs.</summary>
    public UpdatesViewModel Updates { get; }

    /// <summary>The connect screen.</summary>
    public ConnectViewModel Connect { get; }

    [ObservableProperty]
    private object _current;

    [ObservableProperty]
    private BrowserViewModel? _browser;

    [ObservableProperty]
    private PaletteViewModel? _palette;

    /// <summary>Window title.</summary>
    public string Title => Browser is { } b ? $"Portal — {b.Session.Profile.Name}" : "Portal";

    /// <summary>True when <paramref name="profile"/> is the open connection.</summary>
    public bool IsOpen(ServerProfile profile) => Browser?.Session.Profile == profile;

    /// <summary>Saves preferences to disk.</summary>
    public void SavePreferences() => _store.Save(Preferences);

    /// <summary>Saves the server list to disk.</summary>
    public void SaveServers() => _store.Save(Servers.ToList());

    /// <summary>Adds or updates a saved server.</summary>
    public void SaveServer(ServerProfile profile, bool isNew)
    {
        if (isNew)
        {
            Servers.Add(profile);
            Connect.AddCard(profile);
        }
        SaveServers();
        Connect.RefreshCards();
        OnPropertyChanged(nameof(Title));
        Browser?.NotifyProfileChanged();
    }

    /// <summary>Removes a saved server after asking.</summary>
    public async Task RemoveServerAsync(ServerCardViewModel card)
    {
        var ok = await Dialogs.ShowAsync(new MessageDialogViewModel($"Remove {card.Title}?",
            "Portal forgets this server and its pinned folders. Nothing changes on the server, and its key stays in known_hosts.", "Remove", "Cancel"));
        if (!ok) return;
        if (IsOpen(card.Profile)) await DisconnectAsync();
        Servers.Remove(card.Profile);
        Connect.Cards.Remove(card);
        SaveServers();
        Connect.RefreshCards();
    }

    /// <summary>Shows the browser again after visiting the connect screen.</summary>
    public void ShowBrowser()
    {
        if (Browser is not null) Current = Browser;
    }

    /// <summary>Shows the connect screen, keeping the current connection.</summary>
    public void ShowConnect()
    {
        Connect.RefreshCards();
        Current = Connect;
    }

    /// <summary>Connects to the card's server, replacing the current connection.</summary>
    public async Task ConnectAsync(ServerCardViewModel card)
    {
        if (IsOpen(card.Profile))
        {
            ShowBrowser();
            return;
        }
        if (Browser is not null && Transfers.Engine.HasUnfinished)
        {
            var go = await Dialogs.ShowAsync(new MessageDialogViewModel("Transfers are still running",
                $"Connecting to {card.Title} closes the connection to {Browser.Session.Profile.Name} and cancels its unfinished transfers. Partial files are removed.", $"Connect to {card.Title}", "Stay"));
            if (!go) return;
        }
        Current = Connect;
        var ct = card.Begin();
        try
        {
            var session = await SshConnector.ConnectAsync(card.Profile, this, new Progress<ConnectStage>(card.SetStage), ct);
            if (ct.IsCancellationRequested)
            {
                await session.DisposeAsync();
                return;
            }
            Preferences.LastServerId = card.Profile.Id;
            SavePreferences();
            SaveServers();
            await OpenAsync(session);
            card.Reset();
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            card.Reset();
        }
        catch (ConnectCancelledException)
        {
            card.Reset();
        }
        catch (Exception e)
        {
            var target = SshConnector.Resolve(card.Profile);
            var (title, detail) = FriendlyError.ForConnect(e, target.HostName, target.Port, target.User);
            card.Fail(title, detail);
        }
    }

    /// <summary>Closes the current connection and returns to the connect screen.</summary>
    public async Task DisconnectAsync()
    {
        if (Browser is not { } b) return;
        if (Transfers.Engine.HasUnfinished && !await Dialogs.ShowAsync(new MessageDialogViewModel($"Disconnect from {b.Session.Profile.Name}?",
                "Unfinished transfers are cancelled and their partial files removed.", "Disconnect", "Stay")))
            return;
        await CloseBrowserAsync(b);
        Connect.RefreshCards();
        Current = Connect;
        OnPropertyChanged(nameof(Title));
    }

    /// <summary>Connects at startup to a server given on the command line or used last.</summary>
    public async Task StartAsync(string? commandLineTarget, int? commandLinePort)
    {
        ServerCardViewModel? card = null;
        if (commandLineTarget is { Length: > 0 })
        {
            var at = commandLineTarget.IndexOf('@');
            var user = at > 0 ? commandLineTarget[..at] : "";
            var host = at > 0 ? commandLineTarget[(at + 1)..] : commandLineTarget;
            var profile = Servers.FirstOrDefault(s => string.Equals(s.Host, host, StringComparison.OrdinalIgnoreCase) && (user.Length == 0 || s.User == user) && (commandLinePort is null || s.Port == commandLinePort));
            if (profile is null)
            {
                profile = new ServerProfile { Name = host, Host = host, User = user, Port = commandLinePort ?? 0 };
                SaveServer(profile, isNew: true);
            }
            card = Connect.Cards.FirstOrDefault(c => c.Profile == profile);
        }
        else if (Preferences.ReconnectOnLaunch && Preferences.LastServerId is { } id)
        {
            card = Connect.Cards.FirstOrDefault(c => c.Profile.Id == id);
        }
        if (Preferences.CheckForUpdates) _ = Updates.CheckAsync(quiet: true);
        if (card is not null) await ConnectAsync(card);
    }

    /// <summary>Asks before closing the window while transfers run.</summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        if (!Transfers.Engine.HasUnfinished) return true;
        return await Dialogs.ShowAsync(new MessageDialogViewModel("Transfers are still running",
            "Closing Portal stops them. Partial files are kept, but you’ll need to start the transfers again.", "Close Portal", "Keep running"));
    }

    /// <inheritdoc />
    public Task<bool> TrustNewHostAsync(HostKeyInfo key) => Dialogs.ShowAsync(new HostKeyDialogViewModel(key));

    /// <inheritdoc />
    public Task<string?> AskPassphraseAsync(string keyPath, bool retry) => Dialogs.ShowAsync(new SecretDialogViewModel(
        "Unlock your SSH key", $"{Path.GetFileName(keyPath)} is protected with a passphrase. Portal keeps it in memory until you disconnect.", "Passphrase", retry));

    /// <inheritdoc />
    public Task<string?> AskPasswordAsync(string user, string host, bool retry) => Dialogs.ShowAsync(new SecretDialogViewModel(
        $"Sign in to {host}", $"Enter the password for {user}. Portal doesn’t store it.", "Password", retry));

    /// <summary>Shows the browser for an open session, closing the previous one.</summary>
    public async Task OpenAsync(IServerSession session)
    {
        if (Browser is { } old) await CloseBrowserAsync(old);
        var browser = new BrowserViewModel(this, session);
        Browser = browser;
        Current = browser;
        OnPropertyChanged(nameof(Title));
        Transfers.EnsureConnected = browser.EnsureConnectedAsync;
        Transfers.ShowItem = browser.ShowTransferred;
        await browser.StartAsync();
    }

    private async Task CloseBrowserAsync(BrowserViewModel browser)
    {
        var cancelled = await Transfers.Engine.CancelAllUsingAsync(browser.Session.TransferFiles, TimeSpan.FromSeconds(5));
        if (cancelled > 0) Toasts.Show(ToastKind.Info, $"Cancelled {Formatting.Count(cancelled, "transfer")}", $"They belonged to the connection to {browser.Session.Profile.Name}.");
        Browser = null;
        Transfers.EnsureConnected = null;
        Transfers.ShowItem = null;
        browser.Dispose();
        await browser.Session.DisposeAsync();
    }

    private void OnBatchFinished(TransferBatch batch, IReadOnlyList<TransferItem> items)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var done = items.Count(i => i.Status == TransferStatus.Done);
            var failed = items.Count(i => i.Status == TransferStatus.Failed);
            var verb = batch.Direction == TransferDirection.Upload ? "Uploaded" : "Downloaded";
            if (failed > 0)
                Toasts.Show(ToastKind.Error, $"{Formatting.Count(failed, "transfer")} didn’t finish", items.First(i => i.Status == TransferStatus.Failed).Error, "Show transfers", () => Transfers.IsOpen = true);
            else if (done > 0)
                Toasts.Show(ToastKind.Success, $"{verb} {Formatting.Count(done, "item")}", $"{batch.DestinationLabel} · {batch.DestinationDirectory}", "Open folder", () => Browser?.ShowFolder(batch));
            Browser?.OnBatchFinished(batch);
        });
    }
}
