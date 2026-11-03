using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Files;

namespace Portal.ViewModels;

/// <summary>What the details pane shows for the active pane's selection.</summary>
public sealed partial class DetailsViewModel : ObservableObject
{
    private DetailsViewModel(PaneViewModel pane)
    {
        Pane = pane;
    }

    /// <summary>The pane the details describe.</summary>
    public PaneViewModel Pane { get; }

    /// <summary>The single selected entry, if exactly one is selected.</summary>
    public EntryViewModel? Entry { get; private init; }

    /// <summary>Large icon.</summary>
    public string Glyph { get; private init; } = Glyphs.Folder;

    /// <summary>True when the icon is a folder.</summary>
    public bool IsFolderGlyph { get; private init; }

    /// <summary>Name or count.</summary>
    public string Title { get; private init; } = "";

    /// <summary>Kind and size line under the title.</summary>
    public string Subtitle { get; private init; } = "";

    /// <summary>Folder the entry is in.</summary>
    public string Location { get; private init; } = "";

    /// <summary>Full modification date.</summary>
    public string? Modified { get; private init; }

    /// <summary>File size, or the measured folder size.</summary>
    [ObservableProperty]
    private string? _size;

    /// <summary>True while a folder's size is measured on the server.</summary>
    [ObservableProperty]
    private bool _isMeasuring;

    /// <summary>True for a single server folder whose size is not measured yet.</summary>
    [ObservableProperty]
    private bool _canMeasure;

    /// <summary>Owner and group line.</summary>
    public string? Ownership { get; private init; }

    /// <summary>"755 · rwxr-xr-x".</summary>
    public string? PermissionCode { get; private init; }

    /// <summary>Plain-language permission lines.</summary>
    public IReadOnlyList<string> PermissionLines { get; private init; } = [];

    /// <summary>True when a selection exists.</summary>
    public bool HasSelection { get; private init; }

    /// <summary>True for a single file on the server, which can be edited in place.</summary>
    public bool CanEditOnServer { get; private init; }

    /// <summary>Hint shown when nothing is selected.</summary>
    public string? Hint { get; private init; }

    /// <summary>"Download" or "Upload".</summary>
    public string TransferText => Pane.IsRemote ? (Entry is null && HasSelection ? "Download all" : "Download") : (Entry is null && HasSelection ? "Upload all" : "Upload");

    /// <summary>Glyph for <see cref="TransferText"/>.</summary>
    public string TransferGlyph => Pane.IsRemote ? Glyphs.Download : Glyphs.Upload;

    /// <summary>True for server panes, which offer permission editing.</summary>
    public bool IsRemote => Pane.IsRemote;

    /// <summary>Builds the details for the pane's current selection.</summary>
    public static DetailsViewModel For(PaneViewModel pane, string? serverLine)
    {
        var sel = pane.Selected;
        if (sel.Count == 1)
        {
            var e = sel[0];
            var entry = e.Entry;
            return new DetailsViewModel(pane)
            {
                Entry = e,
                HasSelection = true,
                Glyph = e.Glyph,
                IsFolderGlyph = e.IsDirectory,
                Title = e.Name,
                Subtitle = e.IsDirectory ? e.KindText : $"{e.KindText} · {Formatting.Size(entry.Size)}",
                Location = pane.FileSystem.Parent(entry.FullPath) ?? pane.Path,
                Modified = Formatting.FullDate(entry.ModifiedUtc),
                Size = e.IsDirectory ? null : $"{Formatting.Size(entry.Size)} ({entry.Size:N0} bytes)",
                CanMeasure = e.IsDirectory && pane.IsRemote,
                Ownership = entry.Owner is null ? null : $"{entry.Owner} · group {entry.Group}",
                PermissionCode = entry.Mode is int m ? $"{Permissions.ToOctal(m)} · {Permissions.ToSymbolic(m)}" : null,
                PermissionLines = entry.Mode is int mm ? Permissions.Describe(mm) : [],
                CanEditOnServer = pane.IsRemote && !e.IsDirectory,
            };
        }
        if (sel.Count > 1)
        {
            var files = sel.Where(s => !s.IsDirectory).ToList();
            var folders = sel.Count - files.Count;
            var parts = new List<string>();
            if (files.Count > 0) parts.Add($"{Formatting.Count(files.Count, "file")} ({Formatting.Size(files.Sum(f => f.Entry.Size))})");
            if (folders > 0) parts.Add(Formatting.Count(folders, "folder"));
            return new DetailsViewModel(pane)
            {
                HasSelection = true,
                Glyph = Glyphs.Copy,
                IsFolderGlyph = true,
                Title = Formatting.Count(sel.Count, "item"),
                Subtitle = string.Join(" and ", parts),
                Location = pane.Path,
            };
        }
        return new DetailsViewModel(pane)
        {
            Glyph = pane.Path.Length == 0 ? Glyphs.Computer : Glyphs.Folder,
            IsFolderGlyph = pane.Path.Length > 0,
            Title = pane.FolderName,
            Subtitle = Formatting.Count(pane.Items.Count, "item"),
            Location = pane.Path.Length == 0 ? "Drives" : pane.Path,
            Ownership = serverLine,
            Hint = "Select a file or folder to see its details. Shift and Ctrl select several; Ctrl+A selects everything.",
        };
    }
}
