using Portal.Core.Connections;
using Portal.Core.Settings;

namespace Portal.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("portal-settings-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Imports_the_old_portal_sessions_once()
    {
        File.WriteAllText(Path.Combine(_dir, "sessions.json"), """
            [{"host":"tower","user":"root","port":22},{"host":"tower","user":"root","port":22},{"host":"server.example.com","user":"oss","port":2222}]
            """);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), """
            {"default_local_path":"C:\\nowhere","default_remote_path":"/mnt/user","auto_connect":true,"max_parallel_transfers":12,"check_updates_on_launch":false}
            """);
        var store = new SettingsStore(_dir);

        var servers = store.LoadServers();
        var prefs = store.LoadPreferences();

        Assert.Equal(2, servers.Count);
        Assert.Equal("/mnt/user", servers[0].StartPath);
        Assert.Equal(2222, servers[1].Port);
        Assert.Equal(8, prefs.ParallelTransfers);
        Assert.False(prefs.CheckForUpdates);
        Assert.True(prefs.ReconnectOnLaunch);
    }

    [Fact]
    public void Saved_servers_and_preferences_round_trip()
    {
        var store = new SettingsStore(_dir);
        store.Save([new ServerProfile { Name = "Tower", Host = "tower", User = "root", Auth = AuthKind.Password, Pins = ["/mnt/user/media"] }]);
        store.Save(new Preferences { Theme = ThemeChoice.Dark, Conflicts = ConflictPolicy.KeepBoth });

        var again = new SettingsStore(_dir);
        var server = Assert.Single(again.LoadServers());
        Assert.Equal(AuthKind.Password, server.Auth);
        Assert.Equal(["/mnt/user/media"], server.Pins);
        Assert.Equal(ThemeChoice.Dark, again.LoadPreferences().Theme);
        Assert.Equal(ConflictPolicy.KeepBoth, again.LoadPreferences().Conflicts);
        Assert.DoesNotContain(Directory.GetFiles(_dir), f => f.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_files_give_defaults()
    {
        var store = new SettingsStore(_dir);
        Assert.Empty(store.LoadServers());
        Assert.Equal(3, store.LoadPreferences().ParallelTransfers);
        Assert.Equal(1280, store.LoadWindow().Width);
    }
}
