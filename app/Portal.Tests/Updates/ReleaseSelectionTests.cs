using Portal.Core.Updates;

namespace Portal.Tests.Updates;

public class ReleaseSelectionTests
{
    [Theory]
    [InlineData("0.4.2", 0ul, 4ul, 2ul)]
    [InlineData("v1.2.3", 1ul, 2ul, 3ul)]
    public void Parses_three_part_versions(string text, ulong major, ulong minor, ulong patch)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var version));
        Assert.Equal(new ReleaseVersion(major, minor, patch), version);
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("latest")]
    [InlineData("")]
    [InlineData("1.-2.3")]
    [InlineData(" 1.2.3")]
    public void Rejects_anything_but_three_numbers(string text)
    {
        Assert.False(ReleaseVersion.TryParse(text, out _));
    }

    [Fact]
    public void Formats_as_three_numbers()
    {
        Assert.Equal("10.0.7", new ReleaseVersion(10, 0, 7).ToString());
    }

    [Theory]
    [InlineData("0.10.0", "0.9.9", true)]
    [InlineData("1.0.0", "0.99.99", true)]
    [InlineData("0.4.2", "0.4.2", false)]
    [InlineData("0.4.1", "0.4.2", false)]
    [InlineData("nightly", "0.4.2", false)]
    [InlineData("0.5.0", "dev", false)]
    public void Compares_versions_numerically(string candidate, string current, bool newer)
    {
        Assert.Equal(newer, ReleaseVersion.IsNewer(candidate, current));
    }

    [Theory]
    [InlineData("https://git.ossalali.com/oss/Portal/releases/download/0.4.2/portal.exe", true)]
    [InlineData("https://GIT.OSSALALI.COM/x", true)]
    [InlineData("http://git.ossalali.com/x", false)]
    [InlineData("https://example.com/portal.exe", false)]
    [InlineData("https://git.ossalali.com.example.com/portal.exe", false)]
    [InlineData("not a url", false)]
    public void Only_the_release_host_is_trusted(string url, bool trusted)
    {
        Assert.Equal(trusted, UpdatePolicy.IsReleaseHost(url));
    }

    [Fact]
    public void Offers_a_newer_release_with_its_asset()
    {
        var release = Release("0.5.0", "https://git.ossalali.com/oss/Portal/releases/download/0.5.0/portal.exe");

        var info = UpdatePolicy.SelectUpdate(release, "0.4.2");

        Assert.NotNull(info);
        Assert.Equal("0.5.0", info.Version);
        Assert.Equal(20_388_352, info.Size);
        Assert.Equal("https://git.ossalali.com/oss/Portal/releases/tag/0.5.0", info.PageUrl);
    }

    [Fact]
    public void Strips_a_leading_v_from_the_tag()
    {
        var release = Release("v0.5.0", "https://git.ossalali.com/oss/Portal/releases/download/v0.5.0/portal.exe");

        Assert.Equal("0.5.0", UpdatePolicy.SelectUpdate(release, "0.4.2")?.Version);
    }

    [Fact]
    public void Ignores_releases_that_are_not_newer()
    {
        var release = Release("0.4.2", "https://git.ossalali.com/oss/Portal/releases/download/0.4.2/portal.exe");

        Assert.Null(UpdatePolicy.SelectUpdate(release, "0.4.2"));
        Assert.Null(UpdatePolicy.SelectUpdate(release, "0.9.0"));
    }

    [Fact]
    public void Skips_drafts_and_prereleases()
    {
        var release = Release("0.5.0", "https://git.ossalali.com/oss/Portal/releases/download/0.5.0/portal.exe");

        Assert.Null(UpdatePolicy.SelectUpdate(release with { Draft = true }, "0.4.2"));
        Assert.Null(UpdatePolicy.SelectUpdate(release with { Prerelease = true }, "0.4.2"));
    }

    [Fact]
    public void Rejects_an_asset_hosted_elsewhere()
    {
        var release = Release("0.5.0", "https://example.com/portal.exe");

        Assert.Throws<UpdateException>(() => UpdatePolicy.SelectUpdate(release, "0.4.2"));
    }

    [Fact]
    public void Rejects_a_release_page_hosted_elsewhere()
    {
        var release = Release("0.5.0", "https://git.ossalali.com/oss/Portal/releases/download/0.5.0/portal.exe")
            with { HtmlUrl = "https://example.com/release" };

        Assert.Throws<UpdateException>(() => UpdatePolicy.SelectUpdate(release, "0.4.2"));
    }

    [Fact]
    public void Rejects_a_release_without_the_binary()
    {
        var release = Release("0.5.0", "https://git.ossalali.com/oss/Portal/releases/download/0.5.0/portal.exe")
            with { Assets = [] };

        Assert.Throws<UpdateException>(() => UpdatePolicy.SelectUpdate(release, "0.4.2"));
    }

    [Fact]
    public void Rejects_an_empty_binary()
    {
        var release = Release("0.5.0", "https://git.ossalali.com/oss/Portal/releases/download/0.5.0/portal.exe");
        release = release with { Assets = [release.Assets[0] with { Size = 0 }] };

        Assert.Throws<UpdateException>(() => UpdatePolicy.SelectUpdate(release, "0.4.2"));
    }

    [Fact]
    public void Rejects_a_tag_that_is_not_a_version()
    {
        var release = Release("nightly", "https://git.ossalali.com/oss/Portal/releases/download/nightly/portal.exe");

        Assert.Throws<UpdateException>(() => UpdatePolicy.SelectUpdate(release, "0.4.2"));
    }

    private static ReleaseDto Release(string tag, string assetUrl) => new()
    {
        TagName = tag,
        HtmlUrl = $"https://git.ossalali.com/oss/Portal/releases/tag/{tag}",
        Assets =
        [
            new ReleaseAssetDto { Name = "portal.exe", BrowserDownloadUrl = assetUrl, Size = 20_388_352 },
        ],
    };
}
