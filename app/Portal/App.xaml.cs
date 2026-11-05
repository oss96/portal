using System.Windows;
using Portal.Core.Settings;
using Portal.Services;
using Portal.ViewModels;

namespace Portal;

/// <summary>Starts Portal: cleans up after an update, loads settings, opens the window and connects if asked to.</summary>
public partial class App : Application
{
    /// <summary>Switches between light, dark and the Windows setting.</summary>
    public static void ApplyTheme(ThemeChoice theme)
    {
        Current.ThemeMode = theme switch
        {
            ThemeChoice.Light => ThemeMode.Light,
            ThemeChoice.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
        AddDerivedBrushes(Current);
    }

    /// <summary>Adds brushes derived from the Windows accent colour, such as the row selection tint.</summary>
    public static void AddDerivedBrushes(Application app)
    {
        var accent = app.TryFindResource("SystemAccentColor") is System.Windows.Media.Color c ? c : System.Windows.Media.Color.FromRgb(0x00, 0x67, 0xC0);
        var tint = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x3D, accent.R, accent.G, accent.B));
        tint.Freeze();
        app.Resources["SelectionFillBrush"] = tint;
    }

    /// <inheritdoc />
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (Environment.GetEnvironmentVariable("PORTAL_CONFIG_DIR") is { Length: > 0 } config) Core.AppPaths.ConfigDirectory = config;
        if (Environment.GetEnvironmentVariable("PORTAL_SSH_DIR") is { Length: > 0 } ssh) Core.AppPaths.SshDirectory = ssh;
        Updater.CleanupAfterUpdate();
        var (target, port) = CommandLine.Parse(e.Args);
        var store = SettingsStore.Default;
        var main = new MainViewModel(store);
        var startServer = main.PrepareStart(target, port);
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            main.Toasts.Show(ToastKind.Error, "Something went wrong", Core.Connections.FriendlyError.ForOperation(args.Exception));
        };
        TaskScheduler.UnobservedTaskException += (_, args) => args.SetObserved();
        ApplyTheme(main.Preferences.Theme);
        var window = new MainWindow(main, store);
        MainWindow = window;
        window.Show();
        await main.StartAsync(startServer);
    }
}

/// <summary>Reads <c>portal [user@host] [-p port]</c>.</summary>
public static class CommandLine
{
    /// <summary>Returns the connection target and port, either of which may be absent.</summary>
    public static (string? Target, int? Port) Parse(IReadOnlyList<string> args)
    {
        string? target = null;
        int? port = null;
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if ((a is "-p" or "--port") && i + 1 < args.Count && int.TryParse(args[i + 1], out var p))
            {
                port = p;
                i++;
            }
            else if (a.StartsWith("--port=", StringComparison.Ordinal) && int.TryParse(a[7..], out var q)) port = q;
            else if (!a.StartsWith('-') && target is null) target = a;
        }
        return (target, port);
    }
}
