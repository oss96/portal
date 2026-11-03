using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Settings;
using Portal.Services;

namespace Portal.ViewModels;

/// <summary>Update state shown in Settings and as a status-bar chip.</summary>
public sealed partial class UpdatesViewModel(MainViewModel main) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheck))]
    private bool _isChecking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheck), nameof(InstallText))]
    private bool _isAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheck))]
    private bool _isDownloading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheck))]
    private bool _isReady;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallText), nameof(ChipText))]
    private string? _availableVersion;

    [ObservableProperty]
    private string _statusText = $"Portal {Updater.CurrentVersion}";

    /// <summary>True when Check now makes sense.</summary>
    public bool CanCheck => !IsChecking && !IsDownloading && !IsReady && !IsAvailable;

    /// <summary>"Install 1.1.0".</summary>
    public string InstallText => $"Install {AvailableVersion}";

    /// <summary>Status-bar chip text.</summary>
    public string ChipText => IsReady ? "Restart to update" : $"Update {AvailableVersion}";

    /// <summary>True when the status bar should show the update chip.</summary>
    public bool ShowChip => IsAvailable || IsReady;

    /// <summary>Asks the release feed for a newer version.</summary>
    public async Task CheckAsync(bool quiet)
    {
        if (IsChecking || IsDownloading || IsReady) return;
        IsChecking = true;
        StatusText = "Checking for updates…";
        var result = await Updater.CheckAsync(CancellationToken.None);
        IsChecking = false;
        switch (result)
        {
            case UpdateCheck.Available a:
                AvailableVersion = a.Version;
                IsAvailable = true;
                StatusText = $"Portal {a.Version} is available. You have {Updater.CurrentVersion}.";
                break;
            case UpdateCheck.UpToDate:
                StatusText = $"Portal {Updater.CurrentVersion} · up to date";
                if (!quiet) main.Toasts.Show(ToastKind.Success, "Portal is up to date", $"Version {Updater.CurrentVersion} is the newest release.");
                break;
            case UpdateCheck.Failed f:
                StatusText = $"Portal {Updater.CurrentVersion} · couldn’t check: {f.Message}";
                break;
        }
        OnPropertyChanged(nameof(ShowChip));
    }

    /// <summary>Opens the settings dialog.</summary>
    public void ShowSettings() => _ = main.Dialogs.ShowAsync(new SettingsDialogViewModel(main));

    [RelayCommand]
    private Task CheckNow() => CheckAsync(quiet: false);

    [RelayCommand]
    private async Task Install()
    {
        IsAvailable = false;
        IsDownloading = true;
        StatusText = $"Downloading Portal {AvailableVersion}…";
        try
        {
            await Updater.StageAsync(new Progress<double>(p => Progress = p * 100), CancellationToken.None);
            IsReady = true;
            StatusText = $"Portal {AvailableVersion} is ready. Restart to use it.";
        }
        catch (Exception e)
        {
            IsAvailable = true;
            StatusText = $"The download failed: {e.Message}";
        }
        finally
        {
            IsDownloading = false;
            OnPropertyChanged(nameof(ShowChip));
            OnPropertyChanged(nameof(ChipText));
        }
    }

    [RelayCommand]
    private async Task Restart()
    {
        if (!await main.ConfirmCloseAsync()) return;
        Updater.Restart();
    }

    [RelayCommand]
    private void OpenChip() => ShowSettings();
}

/// <summary>The settings dialog. Every change is saved and applied at once.</summary>
public sealed partial class SettingsDialogViewModel(MainViewModel main) : DialogViewModel<bool>(true)
{
    private Preferences Prefs => main.Preferences;

    /// <summary>Update controls.</summary>
    public UpdatesViewModel Updates => main.Updates;

    /// <summary>Choices for parallel transfers.</summary>
    public IReadOnlyList<int> ParallelOptions { get; } = [1, 2, 3, 4, 6, 8];

    /// <summary>Choices for the conflict policy.</summary>
    public IReadOnlyList<Choice<ConflictPolicy>> ConflictOptions { get; } =
        [new(ConflictPolicy.Ask, "Ask me"), new(ConflictPolicy.Replace, "Replace it"), new(ConflictPolicy.Skip, "Skip it"), new(ConflictPolicy.KeepBoth, "Keep both")];

    /// <summary>Theme follows Windows.</summary>
    public bool IsSystemTheme { get => Prefs.Theme == ThemeChoice.System; set { if (value) SetTheme(ThemeChoice.System); } }

    /// <summary>Theme is light.</summary>
    public bool IsLightTheme { get => Prefs.Theme == ThemeChoice.Light; set { if (value) SetTheme(ThemeChoice.Light); } }

    /// <summary>Theme is dark.</summary>
    public bool IsDarkTheme { get => Prefs.Theme == ThemeChoice.Dark; set { if (value) SetTheme(ThemeChoice.Dark); } }

    /// <summary>Transfers at the same time.</summary>
    public int Parallel { get => Prefs.ParallelTransfers; set { Prefs.ParallelTransfers = value; Save(); } }

    /// <summary>Conflict policy.</summary>
    public ConflictPolicy Conflicts { get => Prefs.Conflicts; set { Prefs.Conflicts = value; Save(); } }

    /// <summary>Reconnect at start.</summary>
    public bool Reconnect { get => Prefs.ReconnectOnLaunch; set { Prefs.ReconnectOnLaunch = value; Save(); } }

    /// <summary>Check for updates at start.</summary>
    public bool CheckOnLaunch { get => Prefs.CheckForUpdates; set { Prefs.CheckForUpdates = value; Save(); } }

    [RelayCommand]
    private void Done() => Close(true);

    private void SetTheme(ThemeChoice theme)
    {
        Prefs.Theme = theme;
        App.ApplyTheme(theme);
        Save();
    }

    private void Save() => main.SavePreferences();
}

/// <summary>A value with a label, for combo boxes.</summary>
public sealed record Choice<T>(T Value, string Label);
