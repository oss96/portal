using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Portal.Core;
using Portal.Core.Connections;
using Portal.Core.Files;
using Portal.Core.Settings;
using Portal.Core.Transfers;
using Portal.Services;
using Portal.ViewModels;
using Portal.Views;

namespace Portal.Shots;

/// <summary>Renders Portal's screens offscreen to PNG files, without opening a window, over a fake server.</summary>
public static class Program
{
    private const int Width = 1280;
    private const int Height = 800;

    /// <summary>Writes the scenes to <c>[folder]</c> (default target/shots-wpf) in the theme given by <c>--light</c> or the default dark.</summary>
    [STAThread]
    public static int Main(string[] args)
    {
        var light = args.Contains("--light");
        var folder = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        var output = Path.GetFullPath(folder ?? Path.Combine("target", "shots-wpf"));
        Directory.CreateDirectory(output);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = light ? ThemeMode.Light : ThemeMode.Dark };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/portal;component/Themes/Styles.xaml") });
        app.Resources.MergedDictionaries.Add(new DialogTemplates());
        App.AddDerivedBrushes(app);
        var exit = 0;
        app.Startup += async (_, _) =>
        {
            try
            {
                await new Scenes(output, light ? "-light" : "").RunAsync();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                exit = 1;
            }
            app.Shutdown(exit);
        };
        app.Run();
        return exit;
    }

    private sealed class Scenes(string output, string suffix)
    {
        private readonly List<object> _index = [];
        private string _sandbox = "";
        private MainViewModel _main = null!;
        private FrameworkElement _root = null!;

        public async Task RunAsync()
        {
            _sandbox = Directory.CreateTempSubdirectory("portal-shots-").FullName;
            try
            {
                Prepare();
                await ConnectScenes();
                await BrowserScenes();
                File.WriteAllText(Path.Combine(output, $"index{suffix}.json"), JsonSerializer.Serialize(_index, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"Wrote {_index.Count} scenes to {output}");
            }
            finally
            {
                Directory.Delete(_sandbox, recursive: true);
            }
        }

        private void Prepare()
        {
            var config = Path.Combine(_sandbox, "config");
            var ssh = Path.Combine(_sandbox, "ssh");
            var downloads = Path.Combine(_sandbox, "Downloads");
            Directory.CreateDirectory(config);
            Directory.CreateDirectory(ssh);
            Directory.CreateDirectory(Path.Combine(downloads, "wallpapers"));
            File.WriteAllText(Path.Combine(ssh, "config"), "Host hetzner\n  HostName server.example.com\n  User root\nHost nas\n  HostName nas.local\n");
            foreach (var (name, mb) in new[] { ("Severance S02E01.mkv", 24), ("report-october.pdf", 2), ("poster.jpg", 1), ("Dune Part Two (2024).mkv", 6) })
            {
                using var f = File.Create(Path.Combine(downloads, name));
                f.SetLength(mb * 1024L * 1024);
            }
            AppPaths.ConfigDirectory = config;
            AppPaths.SshDirectory = ssh;
            var store = new SettingsStore(config);
            store.Save(new List<ServerProfile>
            {
                new() { Id = "tower", Name = "Tower", Host = "tower", User = "root", LastConnectedUtc = DateTime.UtcNow.AddHours(-5), StartPath = "/mnt/user/downloads", Pins = ["/mnt/user/media/Movies"] },
                new() { Id = "hetzner", Name = "Hetzner", Host = "hetzner", User = "root", LastConnectedUtc = DateTime.UtcNow.AddDays(-32) },
            });
            store.Save(new Preferences { LocalStartPath = downloads, CheckForUpdates = false, ShowDetails = true });
            _main = new MainViewModel(store);
            var window = new MainWindow(_main, store);
            Application.Current.MainWindow = window;
            var content = (FrameworkElement)window.Content;
            window.Content = null;
            content.Resources.MergedDictionaries.Add(window.Resources);
            content.DataContext = _main;
            _root = new Border { Child = content, Width = Width, Height = Height };
            _root.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
            _root.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");
        }

        private async Task ConnectScenes()
        {
            await Shot("01-connect", "Connect screen with saved servers and SSH config hosts");
            var tower = _main.Connect.Cards[0];
            var hetzner = _main.Connect.Cards[1];
            tower.Begin();
            tower.SetStage(ConnectStage.SigningIn);
            hetzner.Fail("Couldn’t find “server.example.com”", "Check the address, or make sure this PC is on the same network or connected to Tailscale.");
            _main.Connect.BeginEdit(hetzner.Profile);
            await Shot("02-connect-progress-error", "Connecting progress, a connect error, and the edit form");
            tower.Reset();
            hetzner.Reset();
            _main.Connect.CancelEditCommand.Execute(null);
            var hostKey = _main.Dialogs.ShowAsync(new HostKeyDialogViewModel(new HostKeyInfo("tower", 22, "ssh-ed25519", "SHA256:3pQ1mJ2b8vXc0kR7yT5uW9zN4aL6eF0hG2iD8sC1oPq", @"C:\Users\Oss\.ssh\known_hosts")));
            await Shot("03-dialog-host-key", "First-connect host key check");
            _main.Dialogs.Current?.Dismiss();
            await hostKey;
        }

        private async Task BrowserScenes()
        {
            var server = new FakeServer();
            var profile = _main.Servers[0];
            await _main.OpenAsync(new FakeSession(profile, server));
            var b = _main.Browser!;
            await Settle();
            await Select(b.Remote, "Dune Part Two (2024).mkv");
            await Shot("04-browser", "Server first: sidebar, server pane with permissions, details pane");

            b.ShowLocalPane = true;
            b.Activate(b.Local);
            await Select(b.Local, "Severance S02E01.mkv", "poster.jpg");
            await Shot("06-browser-this-pc", "This PC next to the server, two local files selected");

            await b.Remote.NavigateAsync("/mnt/user/appdata");
            b.Activate(b.Remote);
            await Select(b.Remote, "jellyfin", "swag");
            var perm = _main.Dialogs.ShowAsync(new PermissionsDialogViewModel(b.Remote.Selected, server.Accounts));
            await Shot("07-dialog-permissions", "Permissions and owner for two folders");
            _main.Dialogs.Current?.Dismiss();
            await perm;

            var del = _main.Dialogs.ShowAsync(new DeleteDialogViewModel(b.Remote, b.Remote.Selected));
            await Shot("08-dialog-delete", "Delete on the server names what goes and that it is permanent");
            _main.Dialogs.Current?.Dismiss();
            await del;

            var existing = (await server.StatAsync("/mnt/user/downloads/Dune Part Two (2024).mkv", default))!;
            var incoming = (await _main.LocalFiles.StatAsync(Path.Combine(b.Local.Path, "Dune Part Two (2024).mkv"), default))!;
            var conflict = _main.Dialogs.ShowAsync(new ConflictDialogViewModel(new ConflictQuestion(incoming, existing, TransferDirection.Upload, "Tower", "Dune Part Two (2024) (1).mkv", 2)));
            await Shot("09-dialog-conflict", "A file with the same name already exists");
            _main.Dialogs.Current?.Dismiss();
            await conflict;

            _main.Updates.ShowSettings();
            await Shot("10-dialog-settings", "Settings apply right away");
            _main.Dialogs.Current?.Dismiss();

            await b.Remote.NavigateAsync("/mnt/user/media");
            server.FailOnce.Add("Severance");
            var local = await _main.LocalFiles.ListAsync(b.Local.Path, default);
            await _main.Transfers.Engine.EnqueueAsync(_main.LocalFiles, [.. local.Where(e => !e.IsDirectory && e.Name != "Dune Part Two (2024).mkv")], server, "/mnt/user/media", ConflictPolicy.Ask,
                _ => Task.FromResult(new ConflictAnswer(ConflictChoice.Replace, false)), default);
            await Task.Delay(2600);
            _main.Transfers.IsOpen = true;
            await Shot("11-transfers", "Transfer panel: running, finished and failed items");
            _main.Transfers.Engine.PauseAll();
            _main.Transfers.IsOpen = false;

            b.ShowLocalPane = false;
            await b.Remote.NavigateAsync("/mnt/user/downloads/incomplete");
            await Shot("12-empty-folder", "Empty folder state");
            await b.Remote.NavigateAsync("/mnt/user/downloads");
            b.Remote.Filter = "zzz";
            await Shot("13-filter-no-match", "Filter with no matches");
            b.Remote.Filter = "";

            _main.Palette = new PaletteViewModel(b) { Query = "dune" };
            await Task.Delay(600);
            await Shot("14-palette", "Ctrl+K: commands and files on the server");
            _main.Palette = null;

            _main.Toasts.Show(ToastKind.Success, "Uploaded 3 items", "Tower · /mnt/user/media", "Open folder", () => { });
            _main.Toasts.Show(ToastKind.Error, "Couldn’t delete everything", "Permission denied. The account you signed in with isn’t allowed to do this.");
            b.IsConnectionLost = true;
            await Shot("15-toasts-connection-lost", "Toasts and the lost-connection bar");
        }

        private async Task Select(PaneViewModel pane, params string[] names)
        {
            await Settle();
            pane.RequestSelect(names);
            await Settle();
        }

        private async Task Shot(string name, string description)
        {
            name += suffix;
            await Settle();
            _root.Measure(new Size(Width, Height));
            _root.Arrange(new Rect(0, 0, Width, Height));
            _root.UpdateLayout();
            await Settle();
            var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(_root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            await using (var file = File.Create(Path.Combine(output, name + ".png"))) encoder.Save(file);
            _index.Add(new { name, description });
            Console.WriteLine($"  {name}.png  {description}");
        }

        private async Task Settle()
        {
            for (var i = 0; i < 4; i++)
            {
                await Task.Delay(60);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                _root?.UpdateLayout();
            }
        }
    }
}
