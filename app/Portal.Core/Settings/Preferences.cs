namespace Portal.Core.Settings;

/// <summary>Light, dark, or following Windows.</summary>
public enum ThemeChoice
{
    /// <summary>Follow the Windows app mode.</summary>
    System,
    /// <summary>Always light.</summary>
    Light,
    /// <summary>Always dark.</summary>
    Dark,
}

/// <summary>What to do when a transfer would overwrite an existing file.</summary>
public enum ConflictPolicy
{
    /// <summary>Ask every time.</summary>
    Ask,
    /// <summary>Replace the existing file.</summary>
    Replace,
    /// <summary>Leave the existing file and skip the new one.</summary>
    Skip,
    /// <summary>Save the new file under a numbered name.</summary>
    KeepBoth,
}

/// <summary>User preferences, stored in preferences.json.</summary>
public sealed class Preferences
{
    /// <summary>App theme.</summary>
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;

    /// <summary>How many transfers run at the same time, 1 to 8.</summary>
    public int ParallelTransfers { get; set; } = 3;

    /// <summary>Overwrite behaviour for transfers.</summary>
    public ConflictPolicy Conflicts { get; set; } = ConflictPolicy.Ask;

    /// <summary>Reconnect to the last server when Portal starts.</summary>
    public bool ReconnectOnLaunch { get; set; } = true;

    /// <summary>Check git.ossalali.com for a newer release when Portal starts.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Show dot files and hidden Windows files.</summary>
    public bool ShowHidden { get; set; }

    /// <summary>Show This PC next to the server.</summary>
    public bool ShowLocalPane { get; set; }

    /// <summary>Show the details pane.</summary>
    public bool ShowDetails { get; set; } = true;

    /// <summary>Folder This PC opens in.</summary>
    public string LocalStartPath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Id of the server used last.</summary>
    public string? LastServerId { get; set; }

    /// <summary>Clamps values read from disk into their allowed ranges.</summary>
    public Preferences Normalized()
    {
        ParallelTransfers = Math.Clamp(ParallelTransfers, 1, 8);
        return this;
    }
}

/// <summary>Window size and position, stored in window.json.</summary>
public sealed class WindowPlacement
{
    /// <summary>Left edge in device-independent pixels.</summary>
    public double Left { get; set; } = double.NaN;

    /// <summary>Top edge in device-independent pixels.</summary>
    public double Top { get; set; } = double.NaN;

    /// <summary>Width in device-independent pixels.</summary>
    public double Width { get; set; } = 1280;

    /// <summary>Height in device-independent pixels.</summary>
    public double Height { get; set; } = 800;

    /// <summary>Whether the window was maximized.</summary>
    public bool Maximized { get; set; }

    /// <summary>Width of the sidebar.</summary>
    public double SidebarWidth { get; set; } = 232;

    /// <summary>Width of the details pane.</summary>
    public double DetailsWidth { get; set; } = 296;
}
