using System.Text.Json;
using Portal.Core.Connections;

namespace Portal.Core.Settings;

/// <summary>Loads and saves preferences, saved servers and window placement in %APPDATA%\portal.</summary>
public sealed class SettingsStore(string directory)
{
    /// <summary>A store over <see cref="AppPaths.ConfigDirectory"/>.</summary>
    public static SettingsStore Default => new(AppPaths.ConfigDirectory);

    private string PreferencesPath => Path.Combine(directory, "preferences.json");
    private string ServersPath => Path.Combine(directory, "servers.json");
    private string WindowPath => Path.Combine(directory, "window.json");

    /// <summary>Reads preferences, importing the old Portal's settings.json the first time.</summary>
    public Preferences LoadPreferences() =>
        (JsonFile.Load<Preferences>(PreferencesPath) ?? ImportLegacyPreferences() ?? new Preferences()).Normalized();

    /// <summary>Writes preferences.</summary>
    public void Save(Preferences preferences) => JsonFile.Save(PreferencesPath, preferences);

    /// <summary>Reads saved servers, importing the old Portal's sessions.json the first time.</summary>
    public List<ServerProfile> LoadServers() => JsonFile.Load<List<ServerProfile>>(ServersPath) ?? ImportLegacySessions();

    /// <summary>Writes saved servers.</summary>
    public void Save(List<ServerProfile> servers) => JsonFile.Save(ServersPath, servers);

    /// <summary>Reads the window placement.</summary>
    public WindowPlacement LoadWindow() => JsonFile.Load<WindowPlacement>(WindowPath) ?? new WindowPlacement();

    /// <summary>Writes the window placement.</summary>
    public void Save(WindowPlacement placement) => JsonFile.Save(WindowPath, placement);

    internal List<ServerProfile> ImportLegacySessions()
    {
        var path = Path.Combine(directory, "sessions.json");
        var list = new List<ServerProfile>();
        try
        {
            if (!File.Exists(path)) return list;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var remotePath = LegacyString("default_remote_path");
            foreach (var s in doc.RootElement.EnumerateArray())
            {
                var host = s.TryGetProperty("host", out var h) ? h.GetString() ?? "" : "";
                if (host.Length == 0) continue;
                var user = s.TryGetProperty("user", out var u) ? u.GetString() ?? "" : "";
                var port = s.TryGetProperty("port", out var p) && p.TryGetInt32(out var n) ? n : 22;
                if (list.Any(x => x.Host == host && x.User == user && x.Port == port)) continue;
                list.Add(new ServerProfile { Name = host, Host = host, User = user, Port = port, StartPath = string.IsNullOrWhiteSpace(remotePath) ? null : remotePath });
            }
        }
        catch (Exception e) when (e is JsonException or IOException or InvalidOperationException)
        {
        }
        return list;
    }

    internal Preferences? ImportLegacyPreferences()
    {
        var path = Path.Combine(directory, "settings.json");
        try
        {
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var prefs = new Preferences();
            if (root.TryGetProperty("max_parallel_transfers", out var mp) && mp.TryGetInt32(out var n)) prefs.ParallelTransfers = n;
            if (root.TryGetProperty("check_updates_on_launch", out var cu) && cu.ValueKind is JsonValueKind.True or JsonValueKind.False) prefs.CheckForUpdates = cu.GetBoolean();
            if (root.TryGetProperty("auto_connect", out var ac) && ac.ValueKind is JsonValueKind.True or JsonValueKind.False) prefs.ReconnectOnLaunch = ac.GetBoolean();
            if (root.TryGetProperty("default_local_path", out var lp) && lp.GetString() is { Length: > 0 } local && Directory.Exists(local)) prefs.LocalStartPath = local;
            return prefs;
        }
        catch (Exception e) when (e is JsonException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private string? LegacyString(string property)
    {
        var path = Path.Combine(directory, "settings.json");
        try
        {
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return null;
        }
    }
}
