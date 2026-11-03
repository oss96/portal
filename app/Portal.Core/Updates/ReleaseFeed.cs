using System.Text.Json.Serialization;

namespace Portal.Core.Updates;

/// <summary>A release as the Forgejo releases API returns it.</summary>
public sealed record ReleaseDto
{
    /// <summary>The git tag, such as <c>0.5.0</c> or <c>v0.5.0</c>.</summary>
    [JsonPropertyName("tag_name")]
    public required string TagName { get; init; }

    /// <summary>The release page with its notes.</summary>
    [JsonPropertyName("html_url")]
    public required string HtmlUrl { get; init; }

    /// <summary>True for an unpublished draft.</summary>
    [JsonPropertyName("draft")]
    public bool Draft { get; init; }

    /// <summary>True for a release marked as a pre-release.</summary>
    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; init; }

    /// <summary>The files attached to the release.</summary>
    [JsonPropertyName("assets")]
    public IReadOnlyList<ReleaseAssetDto> Assets { get; init; } = [];
}

/// <summary>A file attached to a Forgejo release.</summary>
public sealed record ReleaseAssetDto
{
    /// <summary>The file name, such as <c>portal.exe</c>.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Where the file downloads from.</summary>
    [JsonPropertyName("browser_download_url")]
    public required string BrowserDownloadUrl { get; init; }

    /// <summary>The file size in bytes.</summary>
    [JsonPropertyName("size")]
    public required long Size { get; init; }
}
