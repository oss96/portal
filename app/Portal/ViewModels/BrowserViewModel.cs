using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Connections;
using Portal.Core.Files;
using Portal.Core.Shell;
using Portal.Core.Transfers;
using Portal.Services;

namespace Portal.ViewModels;

/// <summary>The connected screen: sidebar, server pane, optional This PC pane, details and status bar.</summary>
public sealed partial class BrowserViewModel : ObservableObject, IDisposable
{
    private readonly RemoteEditor _editor;
    private (PaneViewModel Pane, IReadOnlyList<FileEntry> Entries, bool Cut)? _clipboard;
    private CancellationTokenSource? _measuring;

    /// <summary>Builds the screen for an open session.</summary>
    public BrowserViewModel(MainViewModel main, IServerSession session)
    {
        Main = main;
        Session = session;
        var prefs = main.Preferences;
        _showLocalPane = prefs.ShowLocalPane;
        _showDetails = prefs.ShowDetails;
        _showHidden = prefs.ShowHidden;
        Remote = new PaneViewModel(session.Files, session.Profile.StartPath ?? session.Files.HomeDirectory, () => ShowHidden, main.Toasts);
        Local = new PaneViewModel(main.LocalFiles, Directory.Exists(prefs.LocalStartPath) ? prefs.LocalStartPath : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), () => ShowHidden, main.Toasts);
        _activePane = Remote;
        Remote.IsActive = true;
        foreach (var pane in new[] { Remote, Local })
        {
            pane.SelectionChanged += () => { if (pane == ActivePane) UpdateDetails(); NotifyCommands(); };
            pane.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PaneViewModel.Path))
                {
                    if (pane == ActivePane) UpdateDetails();
                    if (pane == Remote) MarkCurrentPlace();
                    else RememberLocalPath();
                }
            };
        }
        _details = DetailsViewModel.For(Remote, ServerLine);
        _editor = new RemoteEditor(session, main.Toasts);
        session.ConnectionLost += (_, _) => Application.Current.Dispatcher.InvokeAsync(() => IsConnectionLost = true);
    }

    /// <summary>App-wide state.</summary>
    public MainViewModel Main { get; }

    /// <summary>The open connection.</summary>
    public IServerSession Session { get; }

    /// <summary>The server's file list.</summary>
    public PaneViewModel Remote { get; }

    /// <summary>This PC's file list.</summary>
    public PaneViewModel Local { get; }

    /// <summary>Folders on the server: home, root, shares and pins.</summary>
    public ObservableCollection<SidebarItem> Places { get; } = [];

    /// <summary>Server storage with free space.</summary>
    public ObservableCollection<SidebarItem> Disks { get; } = [];

    /// <summary>Saved servers.</summary>
    public ObservableCollection<SidebarItem> Servers { get; } = [];

    /// <summary>Folders on this PC.</summary>
    public ObservableCollection<SidebarItem> LocalPlaces { get; } = [];

    [ObservableProperty]
    private PaneViewModel _activePane;

    [ObservableProperty]
    private bool _showLocalPane;

    [ObservableProperty]
    private bool _showDetails;

    [ObservableProperty]
    private bool _showHidden;

    [ObservableProperty]
    private DetailsViewModel _details;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionText))]
    private bool _isConnectionLost;

    [ObservableProperty]
    private bool _isReconnecting;

    /// <summary>Name of the server.</summary>
    public string ServerName => Session.Profile.Name;

    /// <summary>"Connected to Tower as root" or the lost-connection notice.</summary>
    public string ConnectionText => IsConnectionLost ? $"Connection to {ServerName} lost" : $"Connected to {ServerName} as {Session.User}";

    /// <summary>"tower:22 · 14 ms".</summary>
    public string ConnectionDetail => $"{Session.HostName}{(Session.Port == 22 ? "" : ":" + Session.Port)} · {Math.Max(1, Math.Round(Session.Latency.TotalMilliseconds))} ms";

    /// <summary>Text of the storage section header.</summary>
    public string StorageHeader => $"Storage on {ServerName}";

    /// <summary>"Upload" or "Download", depending on the active pane.</summary>
    public string TransferText => ActivePane.IsRemote ? "Download" : "Upload";

    /// <summary>Glyph for <see cref="TransferText"/>.</summary>
    public string TransferGlyph => ActivePane.IsRemote ? Glyphs.Download : Glyphs.Upload;

    /// <summary>"3 selected · 1.2 GB on Tower".</summary>
    public string SelectionText
    {
        get
        {
            var sel = ActivePane.Selected;
            var where = ActivePane.IsRemote ? ServerName : "This PC";
            if (sel.Count == 0) return where;
            var bytes = sel.Where(s => !s.IsDirectory).Sum(s => s.Entry.Size);
            return $"{sel.Count} selected{(bytes > 0 ? " · " + Formatting.Size(bytes) : "")} on {where}";
        }
    }

    private string ServerLine => $"{ServerName} · {Session.User}@{Session.HostName}";

    /// <summary>Loads both panes, the sidebar and disk usage.</summary>
    public async Task StartAsync()
    {
        BuildServers();
        BuildLocalPlaces();
        await Task.WhenAll(Remote.StartAsync(), Local.StartAsync());
        await BuildPlacesAsync();
        _ = LoadDisksAsync();
        Remote.Focus();
    }

    /// <summary>Reconnects before a retry when the connection dropped.</summary>
    public async Task EnsureConnectedAsync()
    {
        if (Session.IsConnected) return;
        await Reconnect();
    }

    /// <summary>Opens the destination folder of a finished transfer and selects it.</summary>
    public void ShowTransferred(TransferItem item)
    {
        var pane = item.Direction == TransferDirection.Upload ? Remote : Local;
        if (pane == Local) ShowLocalPane = true;
        Main.Transfers.IsOpen = false;
        Activate(pane);
        _ = pane.NavigateAsync(item.Batch.DestinationDirectory, [item.DestinationName]);
    }

    /// <summary>Opens the destination folder of a finished batch.</summary>
    public void ShowFolder(TransferBatch batch)
    {
        var pane = batch.Direction == TransferDirection.Upload ? Remote : Local;
        if (pane == Local) ShowLocalPane = true;
        Activate(pane);
        _ = pane.NavigateAsync(batch.DestinationDirectory);
    }

    /// <summary>Refreshes the destination pane and disk usage after a batch.</summary>
    public void OnBatchFinished(TransferBatch batch)
    {
        var pane = batch.Direction == TransferDirection.Upload ? Remote : Local;
        if (pane.Path == batch.DestinationDirectory) _ = pane.RefreshAsync();
        if (batch.Direction == TransferDirection.Upload) _ = LoadDisksAsync();
    }

    /// <summary>Makes a pane the target of commands and the details pane.</summary>
    public void Activate(PaneViewModel pane)
    {
        if (ActivePane == pane) return;
        ActivePane = pane;
    }

    /// <summary>Opens a row: folders navigate, files open in their app.</summary>
    public async Task OpenEntryAsync(PaneViewModel pane, EntryViewModel entry)
    {
        if (entry.IsDirectory)
        {
            await pane.NavigateAsync(entry.Path);
            return;
        }
        if (pane.IsRemote) await EditOnServerAsync(entry);
        else
        {
            try
            {
                Process.Start(new ProcessStartInfo(entry.Path) { UseShellExecute = true });
            }
            catch (Exception e)
            {
                Main.Toasts.Show(ToastKind.Error, $"Couldn’t open {entry.Name}", e.Message);
            }
        }
    }

    /// <summary>Finishes an inline rename started by F2 or New folder.</summary>
    public async Task CommitRenameAsync(PaneViewModel pane, EntryViewModel entry, string newName)
    {
        entry.IsRenaming = false;
        newName = newName.Trim();
        if (newName.Length == 0 || newName == entry.Name) return;
        if (newName.Contains('/') || (!pane.IsRemote && newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            Main.Toasts.Show(ToastKind.Error, "That name isn’t allowed", pane.IsRemote ? "Names on the server can’t contain “/”." : "Names on Windows can’t contain \\ / : * ? \" < > |.");
            return;
        }
        if (pane.Items.Any(i => string.Equals(i.Name, newName, pane.IsRemote ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) && i != entry))
        {
            Main.Toasts.Show(ToastKind.Error, $"“{newName}” already exists", "Pick another name.");
            return;
        }
        var parent = pane.FileSystem.Parent(entry.Path) ?? pane.Path;
        await RunAsync($"Couldn’t rename {entry.Name}", async () =>
        {
            await pane.FileSystem.RenameAsync(entry.Path, pane.FileSystem.Combine(parent, newName), overwrite: false, CancellationToken.None);
            await pane.RefreshAsync([newName]);
        });
    }

    /// <summary>Handles a drop of rows from one pane onto another pane or a folder row.</summary>
    public async Task DropEntriesAsync(PaneViewModel source, IReadOnlyList<EntryViewModel> entries, PaneViewModel target, EntryViewModel? onFolder)
    {
        var folder = onFolder is { IsDirectory: true } ? onFolder.Path : target.Path;
        if (source == target)
        {
            if (!target.IsRemote || onFolder is null || entries.Contains(onFolder)) return;
            await CopyOnServerAsync([.. entries.Select(e => e.Entry)], folder, move: true);
            return;
        }
        await TransferAsync(source, [.. entries.Select(e => e.Entry)], target, folder);
    }

    /// <summary>Handles files dropped from Explorer onto a pane.</summary>
    public async Task DropFilesAsync(IReadOnlyList<string> paths, PaneViewModel target, EntryViewModel? onFolder)
    {
        if (!target.IsRemote)
        {
            Main.Toasts.Show(ToastKind.Info, "Drop files on the server side to upload them", "Use Explorer to copy files around this PC.");
            return;
        }
        var entries = new List<FileEntry>();
        foreach (var p in paths)
            if (await Main.LocalFiles.StatAsync(p, CancellationToken.None) is { } e) entries.Add(e);
        var folder = onFolder is { IsDirectory: true } ? onFolder.Path : target.Path;
        await TransferAsync(Local, entries, target, folder);
    }

    /// <summary>Stops edit watchers.</summary>
    public void Dispose()
    {
        _editor.Dispose();
        _measuring?.Cancel();
    }

    [RelayCommand]
    private void ToggleLocalPane() => ShowLocalPane = !ShowLocalPane;

    [RelayCommand]
    private void ToggleDetails() => ShowDetails = !ShowDetails;

    [RelayCommand]
    private void ToggleHidden() => ShowHidden = !ShowHidden;

    [RelayCommand]
    private Task Refresh() => ActivePane.RefreshAsync();

    [RelayCommand]
    private async Task NewFolder()
    {
        var pane = ActivePane;
        if (!pane.IsRemote && pane.Path.Length == 0) return;
        var taken = new HashSet<string>(pane.Items.Select(i => i.Name), pane.IsRemote ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        var name = taken.Contains("New folder") ? TransferEngine.UniqueName(taken, "New folder") : "New folder";
        await RunAsync("Couldn’t create the folder", async () =>
        {
            await pane.FileSystem.CreateDirectoryAsync(pane.FileSystem.Combine(pane.Path, name), CancellationToken.None);
            await pane.RefreshAsync([]);
            pane.RequestSelect([name], rename: true);
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task Transfer()
    {
        var source = ActivePane;
        var target = source.IsRemote ? Local : Remote;
        if (target == Local && Local.Path.Length == 0)
        {
            ShowLocalPane = true;
            Main.Toasts.Show(ToastKind.Info, "Pick a folder on This PC first", "Open a folder on the This PC side, then download again.");
            return Task.CompletedTask;
        }
        if (target == Local && !ShowLocalPane)
            Main.Toasts.Show(ToastKind.Info, $"Downloading to {Local.FolderName}", Local.Path, "Show This PC", () => ShowLocalPane = true);
        return TransferAsync(source, [.. source.Selected.Select(e => e.Entry)], target, target.Path);
    }

    [RelayCommand(CanExecute = nameof(HasSingleSelection))]
    private void Rename()
    {
        if (ActivePane.Selected is [var only]) ActivePane.RequestSelect([only.Name], rename: true);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task Delete()
    {
        var pane = ActivePane;
        var entries = pane.Selected.ToList();
        if (entries.Count == 0) return;
        if (!await Main.Dialogs.ShowAsync(new DeleteDialogViewModel(pane, entries))) return;
        await RunAsync("Couldn’t delete everything", async () =>
        {
            await pane.FileSystem.DeleteAsync([.. entries.Select(e => e.Path)], CancellationToken.None);
            await pane.RefreshAsync([]);
            Main.Toasts.Show(ToastKind.Success, pane.IsRemote ? $"Deleted {Formatting.Count(entries.Count, "item")} from {ServerName}" : $"Moved {Formatting.Count(entries.Count, "item")} to the Recycle Bin", pane.Path);
            if (pane.IsRemote) _ = LoadDisksAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(CanEditPermissions))]
    private async Task EditPermissions()
    {
        var entries = Remote.Selected.ToList();
        if (entries.Count == 0) return;
        var change = await Main.Dialogs.ShowAsync(new PermissionsDialogViewModel(entries, Session.Files.Accounts));
        if (change is null) return;
        if (change.Mode.IsEmpty && change.Owner is null && change.Group is null)
        {
            Main.Toasts.Show(ToastKind.Info, "Nothing changed", "The permissions and owner are as they were.");
            return;
        }
        await RunAsync("Couldn’t change the permissions", async () =>
        {
            await Session.Files.SetPermissionsAsync([.. entries.Select(e => e.Path)], change.Mode, change.Owner, change.Group, change.Recursive, CancellationToken.None);
            await Remote.RefreshAsync();
            var parts = new List<string>();
            if (!change.Mode.IsEmpty) parts.Add($"chmod {change.Mode.ToChmod()}");
            if (change.Owner is not null || change.Group is not null) parts.Add($"owner {change.Owner ?? entries[0].Owner}:{change.Group ?? entries[0].Group}");
            if (change.Recursive) parts.Add("including everything inside");
            Main.Toasts.Show(ToastKind.Success, $"Permissions updated on {Formatting.Count(entries.Count, "item")}", string.Join(" · ", parts));
        });
    }

    [RelayCommand]
    private async Task MeasureSize()
    {
        if (Details.Entry is not { IsDirectory: true } entry || !ActivePane.IsRemote) return;
        _measuring?.Cancel();
        var cts = _measuring = new CancellationTokenSource();
        var details = Details;
        details.IsMeasuring = true;
        details.CanMeasure = false;
        try
        {
            var bytes = await Session.Files.DirectorySizeAsync(entry.Path, cts.Token);
            details.Size = $"{Formatting.Size(bytes)} ({bytes:N0} bytes)";
        }
        catch (Exception e) when (!cts.IsCancellationRequested)
        {
            details.Size = FriendlyError.ForOperation(e);
        }
        finally
        {
            details.IsMeasuring = false;
        }
    }

    [RelayCommand]
    private Task EditOnServer() => Details.Entry is { } e ? EditOnServerAsync(e) : Task.CompletedTask;

    [RelayCommand]
    private void OpenTerminal()
    {
        var p = Session.Profile;
        var ssh = new List<string> { "-t" };
        if (p.User.Length > 0) ssh.AddRange(["-l", p.User]);
        if (p.Port is > 0 and not 22) ssh.AddRange(["-p", p.Port.ToString()]);
        if (p.Auth == AuthKind.KeyFile && p.KeyFile is { Length: > 0 } key) ssh.AddRange(["-i", key]);
        ssh.Add(p.Host);
        var remote = $"cd {PosixShell.Quote(Remote.Path)} && exec \"$SHELL\" -l";
        if (TryStart("wt.exe", ["ssh", .. ssh, remote.Replace(";", "\\;")]) || TryStart("ssh.exe", [.. ssh, remote])) return;
        Main.Toasts.Show(ToastKind.Error, "Couldn’t open a terminal", "Neither Windows Terminal nor the OpenSSH client (ssh.exe) was found.");
    }

    private static bool TryStart(string file, IReadOnlyList<string> args)
    {
        try
        {
            var psi = new ProcessStartInfo(file) { UseShellExecute = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi);
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    [RelayCommand]
    private void CopyPath()
    {
        var paths = ActivePane.Selected.Count > 0 ? ActivePane.Selected.Select(s => s.Path) : [ActivePane.Path];
        Clipboard.SetText(string.Join(Environment.NewLine, paths));
        Main.Toasts.Show(ToastKind.Success, "Path copied", string.Join(", ", paths));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CopyItems() => _clipboard = (ActivePane, [.. ActivePane.Selected.Select(s => s.Entry)], false);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CutItems()
    {
        if (ActivePane.IsRemote) _clipboard = (ActivePane, [.. ActivePane.Selected.Select(s => s.Entry)], true);
        else CopyItems();
    }

    [RelayCommand]
    private async Task Paste()
    {
        var target = ActivePane;
        if (_clipboard is { } c)
        {
            if (c.Pane != target)
            {
                await TransferAsync(c.Pane, c.Entries, target, target.Path);
                return;
            }
            if (!target.IsRemote) return;
            if (c.Cut) _clipboard = null;
            await CopyOnServerAsync(c.Entries, target.Path, move: c.Cut);
            return;
        }
        if (target.IsRemote && Clipboard.ContainsFileDropList())
            await DropFilesAsync([.. Clipboard.GetFileDropList().Cast<string>()], target, null);
    }

    [RelayCommand]
    private async Task PinFolder()
    {
        var path = Remote.Selected is [{ IsDirectory: true } only] ? only.Path : Remote.Path;
        var pins = Session.Profile.Pins;
        if (pins.Contains(path)) return;
        pins.Add(path);
        Main.SaveServers();
        await BuildPlacesAsync();
        Main.Toasts.Show(ToastKind.Success, "Pinned to the sidebar", path);
    }

    [RelayCommand]
    private async Task Unpin(SidebarItem item)
    {
        if (item.Path is null) return;
        Session.Profile.Pins.Remove(item.Path);
        Main.SaveServers();
        await BuildPlacesAsync();
    }

    [RelayCommand]
    private async Task OpenPlace(SidebarItem item)
    {
        switch (item.Target)
        {
            case SidebarTarget.ServerFolder when item.Path is not null:
                Activate(Remote);
                await Remote.NavigateAsync(item.Path);
                break;
            case SidebarTarget.LocalFolder when item.Path is not null:
                ShowLocalPane = true;
                Activate(Local);
                await Local.NavigateAsync(item.Path);
                break;
            case SidebarTarget.Server when item.Path is not null:
                if (Main.Connect.Cards.FirstOrDefault(c => c.Profile.Id == item.Path) is { } card) await Main.ConnectAsync(card);
                break;
            case SidebarTarget.AddServer:
                Main.ShowConnect();
                break;
        }
    }

    [RelayCommand]
    private async Task Reconnect()
    {
        if (IsReconnecting) return;
        IsReconnecting = true;
        try
        {
            await Session.ReconnectAsync(CancellationToken.None);
            IsConnectionLost = false;
            OnPropertyChanged(nameof(ConnectionDetail));
            await Remote.RefreshAsync();
        }
        catch (Exception e)
        {
            var (title, detail) = FriendlyError.ForConnect(e, Session.HostName, Session.Port, Session.User);
            Main.Toasts.Show(ToastKind.Error, title, detail);
        }
        finally
        {
            IsReconnecting = false;
        }
    }

    [RelayCommand]
    private Task Disconnect() => Main.DisconnectAsync();

    [RelayCommand]
    private void OpenPalette() => Main.Palette = new PaletteViewModel(this);

    [RelayCommand]
    private void OpenSettings() => Main.Updates.ShowSettings();

    [RelayCommand]
    private void SwitchPane() => Activate(ActivePane == Remote && ShowLocalPane ? Local : Remote);

    private bool HasSelection() => ActivePane.Selected.Count > 0;

    private bool HasSingleSelection() => ActivePane.Selected.Count == 1;

    private bool CanEditPermissions() => ActivePane.IsRemote && ActivePane.Selected.Count > 0;

    partial void OnActivePaneChanged(PaneViewModel? oldValue, PaneViewModel newValue)
    {
        if (oldValue is not null) oldValue.IsActive = false;
        newValue.IsActive = true;
        UpdateDetails();
        NotifyCommands();
        OnPropertyChanged(nameof(TransferText));
        OnPropertyChanged(nameof(TransferGlyph));
    }

    partial void OnShowLocalPaneChanged(bool value)
    {
        Main.Preferences.ShowLocalPane = value;
        Main.SavePreferences();
        if (!value && ActivePane == Local) Activate(Remote);
    }

    partial void OnShowDetailsChanged(bool value)
    {
        Main.Preferences.ShowDetails = value;
        Main.SavePreferences();
    }

    partial void OnShowHiddenChanged(bool value)
    {
        Main.Preferences.ShowHidden = value;
        Main.SavePreferences();
        Remote.RefreshFilter();
        Local.RefreshFilter();
    }

    private void RememberLocalPath()
    {
        if (Local.Path.Length == 0) return;
        Main.Preferences.LocalStartPath = Local.Path;
        Main.SavePreferences();
    }

    private async Task TransferAsync(PaneViewModel source, IReadOnlyList<FileEntry> entries, PaneViewModel target, string folder)
    {
        if (entries.Count == 0) return;
        if (target == Local && folder.Length == 0)
        {
            Main.Toasts.Show(ToastKind.Info, "Pick a folder on This PC first", "Open a folder on the This PC side, then download again.");
            return;
        }
        await RunAsync("Couldn’t start the transfer", async () =>
        {
            await EnsureConnectedAsync();
            IFileSystem from = source.IsRemote ? Session.TransferFiles : Main.LocalFiles;
            IFileSystem to = target.IsRemote ? Session.TransferFiles : Main.LocalFiles;
            var batch = await Main.Transfers.Engine.EnqueueAsync(from, entries, to, folder, Main.Preferences.Conflicts,
                q => Main.Dialogs.ShowAsync(new ConflictDialogViewModel(q)), CancellationToken.None);
            if (batch is null) return;
            var verb = batch.Direction == TransferDirection.Upload ? "Uploading" : "Downloading";
            Main.Toasts.Show(ToastKind.Info, $"{verb} {Formatting.Count(batch.Count, "item")}", $"{(batch.Direction == TransferDirection.Upload ? "To" : "From")} {ServerName} · {folder}", "Show transfers", () => Main.Transfers.IsOpen = true);
        });
    }

    private async Task CopyOnServerAsync(IReadOnlyList<FileEntry> entries, string folder, bool move)
    {
        if (entries.Count == 0) return;
        if (!Session.Files.CanRunCommands && !move)
        {
            Main.Toasts.Show(ToastKind.Error, "Can’t copy on this server", "This account may only transfer files (SFTP only). Download and upload the files instead.");
            return;
        }
        await RunAsync(move ? "Couldn’t move everything" : "Couldn’t copy everything", async () =>
        {
            var result = await ServerCopy.RunAsync(Session.Files, entries, folder, move, Main.Preferences.Conflicts,
                q => Main.Dialogs.ShowAsync(new ConflictDialogViewModel(q)), CancellationToken.None);
            if (Remote.Path == folder) await Remote.RefreshAsync(result.Placed);
            else await Remote.RefreshAsync();
            if (result.Placed.Count == 0) return;
            var skipped = result.Skipped > 0 ? $" · {Formatting.Count(result.Skipped, "item")} skipped" : "";
            Main.Toasts.Show(ToastKind.Success, $"{(move ? "Moved" : "Copied")} {Formatting.Count(result.Placed.Count, "item")} on {ServerName}", folder + skipped);
        });
    }

    private async Task EditOnServerAsync(EntryViewModel entry)
    {
        await RunAsync($"Couldn’t open {entry.Name}", () => _editor.OpenAsync(entry.Entry));
    }

    private async Task RunAsync(string failureTitle, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception e)
        {
            Main.Toasts.Show(ToastKind.Error, failureTitle, FriendlyError.ForOperation(e));
        }
    }

    private void UpdateDetails()
    {
        _measuring?.Cancel();
        Details = DetailsViewModel.For(ActivePane, ActivePane.IsRemote ? ServerLine : null);
        OnPropertyChanged(nameof(SelectionText));
    }

    private void NotifyCommands()
    {
        TransferCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        EditPermissionsCommand.NotifyCanExecuteChanged();
        CopyItemsCommand.NotifyCanExecuteChanged();
        CutItemsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SelectionText));
    }

    private void BuildServers()
    {
        Servers.Clear();
        foreach (var s in Main.Servers)
            Servers.Add(new SidebarItem(SidebarTarget.Server, s.Name, Glyphs.Server, s.Id) { IsConnected = s == Session.Profile, IsCurrent = s == Session.Profile });
    }

    private void BuildLocalPlaces()
    {
        LocalPlaces.Clear();
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var (label, path) in new[] { ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.Desktop)), ("Downloads", Path.Combine(profile, "Downloads")), ("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)) })
            if (Directory.Exists(path)) LocalPlaces.Add(new SidebarItem(SidebarTarget.LocalFolder, label, Glyphs.Folder, path));
        foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable))
            LocalPlaces.Add(new SidebarItem(SidebarTarget.LocalFolder, string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.Name.TrimEnd('\\') : $"{d.VolumeLabel} ({d.Name.TrimEnd('\\')})", Glyphs.Drive, d.Name));
    }

    private async Task BuildPlacesAsync()
    {
        var items = new List<SidebarItem>
        {
            new(SidebarTarget.ServerFolder, "Home", Glyphs.Home, Session.Files.HomeDirectory),
        };
        if (Session.Files.HomeDirectory != "/") items.Add(new SidebarItem(SidebarTarget.ServerFolder, "Root (/)", Glyphs.Folder, "/"));
        try
        {
            if (await Session.Files.StatAsync("/mnt/user", CancellationToken.None) is { IsDirectory: true })
            {
                items.Add(new SidebarItem(SidebarTarget.ServerFolder, "Shares", Glyphs.Server, "/mnt/user"));
                foreach (var share in (await Session.Files.ListAsync("/mnt/user", CancellationToken.None)).Where(e => e.IsDirectory && !e.IsHidden).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                    items.Add(new SidebarItem(SidebarTarget.ServerFolder, share.Name, Glyphs.Folder, share.FullPath));
            }
        }
        catch (Exception)
        {
        }
        foreach (var pin in Session.Profile.Pins.Where(p => items.All(i => i.Path != p)))
            items.Add(new SidebarItem(SidebarTarget.ServerFolder, RemotePath.NameOf(pin), Glyphs.Pin, pin) { IsPin = true });
        Places.Clear();
        foreach (var i in items) Places.Add(i);
        MarkCurrentPlace();
    }

    private void MarkCurrentPlace()
    {
        foreach (var p in Places) p.IsCurrent = p.Path == Remote.Path;
    }

    private async Task LoadDisksAsync()
    {
        try
        {
            var disks = await Session.Files.DisksAsync(CancellationToken.None);
            var ordered = disks.OrderBy(d => d.MountPoint switch { "/mnt/user" => 0, "/mnt/cache" => 1, "/" => 2, _ => 3 }).ThenBy(d => d.MountPoint, StringComparer.Ordinal).Take(6);
            Disks.Clear();
            foreach (var d in ordered)
                Disks.Add(new SidebarItem(SidebarTarget.Disk, d.MountPoint == "/mnt/user" ? "Shares (/mnt/user)" : d.MountPoint, Glyphs.Drive, d.MountPoint)
                {
                    Meta = $"{Formatting.Size(d.FreeBytes)} free",
                    UsedPercent = d.UsedFraction * 100,
                });
        }
        catch (Exception)
        {
        }
    }
}
