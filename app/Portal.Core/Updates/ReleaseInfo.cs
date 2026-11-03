namespace Portal.Core.Updates;

/// <summary>A stable release that is newer than the running binary.</summary>
/// <param name="Version">The version as <c>X.Y.Z</c>.</param>
/// <param name="PageUrl">The release page with its notes.</param>
/// <param name="DownloadUrl">Where <c>portal.exe</c> downloads from.</param>
/// <param name="Size">The announced size of <c>portal.exe</c> in bytes.</param>
public sealed record ReleaseInfo(string Version, string PageUrl, string DownloadUrl, long Size);
