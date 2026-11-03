using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Files;

namespace Portal.ViewModels;

/// <summary>One row in a file list.</summary>
public sealed partial class EntryViewModel(FileEntry entry) : ObservableObject
{
    private static readonly HashSet<string> VideoExt = ["mkv", "mp4", "avi", "mov", "webm", "m4v", "ts"];
    private static readonly HashSet<string> PictureExt = ["jpg", "jpeg", "png", "gif", "webp", "bmp", "svg", "heic"];
    private static readonly HashSet<string> MusicExt = ["mp3", "flac", "wav", "ogg", "m4a", "opus"];
    private static readonly HashSet<string> PackageExt = ["zip", "tar", "gz", "tgz", "zst", "xz", "7z", "rar", "iso", "img", "deb", "rpm"];
    private static readonly HashSet<string> CodeExt = ["sh", "yaml", "yml", "json", "toml", "ini", "cfg", "conf", "xml", "py", "js", "ts", "cs", "rs", "go", "env", "plg", "service", "md", "txt", "log"];

    [ObservableProperty]
    private FileEntry _entry = entry;

    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private string _editName = entry.Name;

    /// <summary>File or folder name.</summary>
    public string Name => Entry.Name;

    /// <summary>Full path.</summary>
    public string Path => Entry.FullPath;

    /// <summary>True for folders and links to folders.</summary>
    public bool IsDirectory => Entry.IsDirectory;

    /// <summary>True for dot files and hidden Windows files.</summary>
    public bool IsHidden => Entry.IsHidden;

    /// <summary>Icon glyph for the kind of file.</summary>
    public string Glyph
    {
        get
        {
            if (Entry.IsDirectory) return Glyphs.Folder;
            if (Entry.IsLink) return Glyphs.Link;
            var ext = Extension;
            if (VideoExt.Contains(ext)) return Glyphs.Video;
            if (PictureExt.Contains(ext)) return Glyphs.Picture;
            if (MusicExt.Contains(ext)) return Glyphs.Music;
            if (PackageExt.Contains(ext)) return Glyphs.Package;
            if (CodeExt.Contains(ext) || !Name.Contains('.')) return Glyphs.Code;
            return Glyphs.Document;
        }
    }

    /// <summary>"Folder", "MKV file", and so on.</summary>
    public string KindText => Entry.Kind switch
    {
        EntryKind.Directory => "Folder",
        EntryKind.LinkToDirectory => "Link to a folder",
        EntryKind.LinkToFile => "Link to a file",
        EntryKind.Special => "Device or socket",
        _ => Extension.Length > 0 ? $"{Extension.ToUpperInvariant()} file" : "File",
    };

    /// <summary>Short modification date.</summary>
    public string ModifiedText => Formatting.Date(Entry.ModifiedUtc);

    /// <summary>Full modification date for the tooltip.</summary>
    public string ModifiedTooltip => Formatting.FullDate(Entry.ModifiedUtc);

    /// <summary>Size for files; blank for folders.</summary>
    public string SizeText => Entry.IsDirectory ? "" : Formatting.Size(Entry.Size);

    /// <summary>"drwxr-xr-x" style mode, or blank on this PC.</summary>
    public string ModeText => Entry.Mode is int m ? (Entry.IsDirectory ? "d" : Entry.IsLink ? "l" : "-") + Permissions.ToSymbolic(m) : "";

    /// <summary>Plain-language permissions for the tooltip.</summary>
    public string ModeTooltip => Entry.Mode is int m ? $"{Permissions.ToOctal(m)}\n{string.Join("\n", Permissions.Describe(m))}" : "";

    /// <summary>Owner name.</summary>
    public string Owner => Entry.Owner ?? "";

    /// <summary>Group name.</summary>
    public string Group => Entry.Group ?? "";

    /// <summary>Sort key that keeps folders first.</summary>
    public int KindOrder => Entry.IsDirectory ? 0 : 1;

    private string Extension
    {
        get
        {
            var dot = Name.LastIndexOf('.');
            return dot > 0 ? Name[(dot + 1)..].ToLowerInvariant() : "";
        }
    }

    partial void OnEntryChanged(FileEntry value)
    {
        EditName = value.Name;
        OnPropertyChanged(string.Empty);
    }
}
