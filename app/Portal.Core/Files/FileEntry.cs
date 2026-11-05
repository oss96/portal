namespace Portal.Core.Files;

/// <summary>What kind of thing a directory entry is.</summary>
public enum EntryKind
{
    /// <summary>A regular file.</summary>
    File,
    /// <summary>A directory.</summary>
    Directory,
    /// <summary>A symbolic link whose target is a file, or a broken link.</summary>
    LinkToFile,
    /// <summary>A symbolic link whose target is a directory.</summary>
    LinkToDirectory,
    /// <summary>A device, socket or pipe.</summary>
    Special,
}

/// <summary>One file or folder as listed by a <see cref="IFileSystem"/>.</summary>
public sealed record FileEntry(string Name, string FullPath, EntryKind Kind, long Size, DateTime ModifiedUtc)
{
    /// <summary>Unix mode bits (permissions plus setuid, setgid, sticky); null on Windows.</summary>
    public int? Mode { get; init; }

    /// <summary>Owner account name, or the numeric id when the name is unknown.</summary>
    public string? Owner { get; init; }

    /// <summary>Owning group name, or the numeric id when the name is unknown.</summary>
    public string? Group { get; init; }

    /// <summary>True for dot files on servers and for files Windows marks hidden.</summary>
    public bool IsHidden { get; init; }

    /// <summary>True when the entry can be opened as a folder.</summary>
    public bool IsDirectory => Kind is EntryKind.Directory or EntryKind.LinkToDirectory;

    /// <summary>True for symbolic links.</summary>
    public bool IsLink => Kind is EntryKind.LinkToFile or EntryKind.LinkToDirectory;
}

/// <summary>One step of a breadcrumb trail.</summary>
public sealed record PathSegment(string Label, string Path);
