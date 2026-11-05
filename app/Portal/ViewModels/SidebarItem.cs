using CommunityToolkit.Mvvm.ComponentModel;

namespace Portal.ViewModels;

/// <summary>What clicking a sidebar entry does.</summary>
public enum SidebarTarget
{
    /// <summary>Opens a folder on the server.</summary>
    ServerFolder,
    /// <summary>Opens a folder on this PC next to the server.</summary>
    LocalFolder,
    /// <summary>Connects to or switches to a saved server.</summary>
    Server,
    /// <summary>Opens the connect screen to add a server.</summary>
    AddServer,
    /// <summary>Shows a disk; not clickable.</summary>
    Disk,
}

/// <summary>One entry of the sidebar.</summary>
public sealed partial class SidebarItem(SidebarTarget target, string label, string glyph, string? path = null) : ObservableObject
{
    /// <summary>What the entry does.</summary>
    public SidebarTarget Target { get; } = target;

    /// <summary>Text.</summary>
    public string Label { get; } = label;

    /// <summary>Icon.</summary>
    public string Glyph { get; } = glyph;

    /// <summary>Folder path, or the server id for <see cref="SidebarTarget.Server"/>.</summary>
    public string? Path { get; } = path;

    /// <summary>True for folders the user pinned, which can be unpinned.</summary>
    public bool IsPin { get; init; }

    /// <summary>Right-aligned note, such as free space.</summary>
    public string? Meta { get; init; }

    /// <summary>Disk usage, 0 to 100, for disks.</summary>
    public double UsedPercent { get; init; }

    /// <summary>True when a disk is more than 85 % full.</summary>
    public bool IsNearlyFull => UsedPercent > 85;

    /// <summary>True for disks.</summary>
    public bool IsDisk => Target == SidebarTarget.Disk;

    /// <summary>For servers: whether it is the open connection.</summary>
    public bool IsConnected { get; init; }

    /// <summary>For servers: whether to show the connection dot.</summary>
    public bool IsServer => Target == SidebarTarget.Server;

    [ObservableProperty]
    private bool _isCurrent;
}
