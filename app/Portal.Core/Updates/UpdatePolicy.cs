namespace Portal.Core.Updates;

/// <summary>Which releases Portal may install and where they may come from.</summary>
public static class UpdatePolicy
{
    /// <summary>The only host allowed to serve release metadata and binaries.</summary>
    public const string ReleaseHost = "git.ossalali.com";

    /// <summary>The Forgejo endpoint for the latest stable release of Portal.</summary>
    public const string LatestReleaseApi = "https://git.ossalali.com/api/v1/repos/oss/Portal/releases/latest";

    /// <summary>The release asset that holds the Windows binary.</summary>
    public const string AssetName = "portal.exe";

    /// <summary>True for an https URL on <see cref="ReleaseHost"/>.</summary>
    public static bool IsReleaseHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals(ReleaseHost, StringComparison.OrdinalIgnoreCase);

    /// <summary>The release to offer, or null when it is a draft, a pre-release or not newer than <paramref name="currentVersion"/>.</summary>
    /// <exception cref="UpdateException">The release is newer but unusable.</exception>
    public static ReleaseInfo? SelectUpdate(ReleaseDto release, string currentVersion)
    {
        if (release.Draft || release.Prerelease)
        {
            return null;
        }

        var version = release.TagName.StartsWith('v') ? release.TagName[1..] : release.TagName;
        if (!ReleaseVersion.TryParse(version, out _))
        {
            throw new UpdateException($"release tag '{release.TagName}' is not X.Y.Z");
        }

        if (!ReleaseVersion.IsNewer(version, currentVersion))
        {
            return null;
        }

        var asset = release.Assets.FirstOrDefault(a => a.Name == AssetName)
            ?? throw new UpdateException($"release {version} has no {AssetName} asset");
        if (asset.Size <= 0)
        {
            throw new UpdateException($"release {version} reports an empty {AssetName}");
        }

        if (!IsReleaseHost(asset.BrowserDownloadUrl) || !IsReleaseHost(release.HtmlUrl))
        {
            throw new UpdateException($"release {version} points outside {ReleaseHost}");
        }

        return new ReleaseInfo(version, release.HtmlUrl, asset.BrowserDownloadUrl, asset.Size);
    }
}
