namespace Portal.Core;

/// <summary>Locations Portal reads and writes on this PC.</summary>
public static class AppPaths
{
    /// <summary>Folder for Portal's settings and saved servers, %APPDATA%\portal.</summary>
    public static string ConfigDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "portal");

    /// <summary>The user's OpenSSH folder, %USERPROFILE%\.ssh.</summary>
    public static string SshDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
}
