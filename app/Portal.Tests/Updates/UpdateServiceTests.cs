using System.Net;
using System.Text;
using Portal.Core.Updates;

namespace Portal.Tests.Updates;

public class UpdateServiceTests
{
    private const string DownloadUrl = "https://git.ossalali.com/oss/Portal/releases/download/0.5.0/portal.exe";
    private static readonly byte[] OldBinary = Encoding.ASCII.GetBytes("MZ standing in for the running binary");

    [Fact]
    public async Task Check_offers_a_newer_release()
    {
        using var service = Service(new FakeHttpHandler().Json(UpdatePolicy.LatestReleaseApi, Feed("0.5.0", 1234)));

        var result = await service.CheckAsync("0.4.2");

        var available = Assert.IsType<UpdateCheckResult.Available>(result);
        Assert.Equal(new ReleaseInfo("0.5.0", "https://git.ossalali.com/oss/Portal/releases/tag/0.5.0", DownloadUrl, 1234), available.Release);
    }

    [Fact]
    public async Task Check_is_up_to_date_when_the_release_is_not_newer()
    {
        using var service = Service(new FakeHttpHandler().Json(UpdatePolicy.LatestReleaseApi, Feed("0.5.0", 1234)));

        Assert.IsType<UpdateCheckResult.UpToDate>(await service.CheckAsync("0.5.0"));
    }

    [Fact]
    public async Task Check_is_up_to_date_when_nothing_is_published()
    {
        using var service = Service(new FakeHttpHandler().Status(UpdatePolicy.LatestReleaseApi, HttpStatusCode.NotFound));

        Assert.IsType<UpdateCheckResult.UpToDate>(await service.CheckAsync("0.4.2"));
    }

    [Fact]
    public async Task Check_reports_a_server_error()
    {
        using var service = Service(new FakeHttpHandler().Status(UpdatePolicy.LatestReleaseApi, HttpStatusCode.InternalServerError));

        var error = Assert.IsType<UpdateCheckResult.Error>(await service.CheckAsync("0.4.2"));
        Assert.StartsWith("Update check failed: Forgejo rejected the request (HTTP 500", error.Message);
    }

    [Fact]
    public async Task Check_reports_an_unreachable_server()
    {
        using var service = Service(new FakeHttpHandler());

        var error = Assert.IsType<UpdateCheckResult.Error>(await service.CheckAsync("0.4.2"));
        Assert.StartsWith("Update check failed: request failed", error.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "html_url": "https://git.ossalali.com/x" }""")]
    [InlineData("""{ "tag_name": null, "html_url": "https://git.ossalali.com/x" }""")]
    public async Task Check_reports_an_unexpected_feed(string body)
    {
        using var service = Service(new FakeHttpHandler().Json(UpdatePolicy.LatestReleaseApi, body));

        var error = Assert.IsType<UpdateCheckResult.Error>(await service.CheckAsync("0.4.2"));
        Assert.StartsWith("Update check failed: unexpected release feed", error.Message);
    }

    [Fact]
    public async Task Check_reports_a_release_hosted_elsewhere()
    {
        var feed = Feed("0.5.0", 1234).Replace(DownloadUrl, "https://example.com/portal.exe", StringComparison.Ordinal);
        using var service = Service(new FakeHttpHandler().Json(UpdatePolicy.LatestReleaseApi, feed));

        var error = Assert.IsType<UpdateCheckResult.Error>(await service.CheckAsync("0.4.2"));
        Assert.Equal("Update check failed: release 0.5.0 points outside git.ossalali.com", error.Message);
    }

    [Fact]
    public async Task Check_identifies_itself_with_the_portal_version_only()
    {
        var handler = new FakeHttpHandler().Status(UpdatePolicy.LatestReleaseApi, HttpStatusCode.NotFound);
        using var service = Service(handler);

        await service.CheckAsync("0.4.2");

        var request = Assert.Single(handler.Requests);
        Assert.Equal($"Portal/{UpdateService.CurrentVersion}", request.Headers.UserAgent.ToString());
        Assert.Matches(@"^\d+\.\d+\.\d+", UpdateService.CurrentVersion);
    }

    [Fact]
    public async Task Download_swaps_the_new_binary_in_and_keeps_the_old_one_aside()
    {
        using var temp = new TempDirectory();
        var exe = RunningExe(temp);
        var newBinary = NewBinary(5000);
        var progress = new RecordingProgress();
        using var service = Service(new FakeHttpHandler().Bytes(DownloadUrl, newBinary), exe);

        await service.DownloadAndStageAsync(Info(newBinary.Length), progress);

        Assert.Equal(newBinary, File.ReadAllBytes(exe));
        Assert.Equal(OldBinary, File.ReadAllBytes(exe + ".old"));
        Assert.False(File.Exists(exe + ".new"));
        Assert.Equal(1.0, progress.Values[^1]);
        Assert.All(progress.Values, value => Assert.InRange(value, 0.0, 1.0));
    }

    [Fact]
    public async Task Download_rejects_a_file_larger_than_announced()
    {
        using var temp = new TempDirectory();
        var exe = RunningExe(temp);
        using var service = Service(new FakeHttpHandler().Bytes(DownloadUrl, NewBinary(5000)), exe);

        var error = await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAndStageAsync(Info(4000)));

        Assert.Equal("Download failed: download is larger than the announced 4000 bytes", error.Message);
        AssertUntouched(exe);
    }

    [Fact]
    public async Task Download_rejects_an_incomplete_file()
    {
        using var temp = new TempDirectory();
        var exe = RunningExe(temp);
        using var service = Service(new FakeHttpHandler().Bytes(DownloadUrl, NewBinary(3000)), exe);

        var error = await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAndStageAsync(Info(4000)));

        Assert.Equal("Download failed: download is incomplete: 3000 of 4000 bytes", error.Message);
        AssertUntouched(exe);
    }

    [Fact]
    public async Task Download_rejects_a_file_that_is_not_an_executable()
    {
        using var temp = new TempDirectory();
        var exe = RunningExe(temp);
        var page = Encoding.ASCII.GetBytes("<html>error page</html>");
        using var service = Service(new FakeHttpHandler().Bytes(DownloadUrl, page), exe);

        var error = await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAndStageAsync(Info(page.Length)));

        Assert.Equal("Download failed: the downloaded file is not a Windows executable", error.Message);
        AssertUntouched(exe);
    }

    [Fact]
    public async Task Download_reports_a_rejected_request()
    {
        using var temp = new TempDirectory();
        var exe = RunningExe(temp);
        using var service = Service(new FakeHttpHandler().Status(DownloadUrl, HttpStatusCode.Forbidden), exe);

        var error = await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAndStageAsync(Info(4000)));

        Assert.StartsWith("Download failed: Forgejo rejected the download (HTTP 403", error.Message);
        AssertUntouched(exe);
    }

    [Fact]
    public async Task Download_refuses_a_url_outside_the_release_host_without_requesting_it()
    {
        using var temp = new TempDirectory();
        var exe = RunningExe(temp);
        var handler = new FakeHttpHandler();
        using var service = Service(handler, exe);

        await Assert.ThrowsAsync<UpdateException>(
            () => service.DownloadAndStageAsync(Info(4000) with { DownloadUrl = "https://example.com/portal.exe" }));

        Assert.Empty(handler.Requests);
        AssertUntouched(exe);
    }

    [Fact]
    public async Task Download_stops_on_cancellation_and_removes_the_partial_file()
    {
        using var temp = new TempDirectory();
        var exe = RunningExe(temp);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var service = Service(new FakeHttpHandler().Bytes(DownloadUrl, NewBinary(5000)), exe);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.DownloadAndStageAsync(Info(5000), cancellationToken: cancellation.Token));

        AssertUntouched(exe);
    }

    [Fact]
    public async Task Install_leaves_the_running_binary_in_place_when_it_cannot_be_moved_aside()
    {
        using var temp = new TempDirectory();
        var exe = RunningExe(temp);
        var newBinary = NewBinary(5000);
        using var service = Service(new FakeHttpHandler().Route(DownloadUrl, () =>
        {
            Directory.CreateDirectory(exe + ".old");
            File.WriteAllText(Path.Combine(exe + ".old", "locked"), "keeps the folder from being deleted");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(newBinary) };
        }), exe);

        var error = await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAndStageAsync(Info(newBinary.Length)));

        Assert.StartsWith("Install failed: cannot move", error.Message);
        Assert.Equal(OldBinary, File.ReadAllBytes(exe));
    }

    [Fact]
    public void Cleanup_removes_the_leftovers_of_a_previous_update()
    {
        using var temp = new TempDirectory();
        var exe = RunningExe(temp);
        File.WriteAllText(exe + ".old", "old");
        File.WriteAllText(exe + ".new", "new");

        UpdateService.CleanupLeftovers(exe);

        Assert.False(File.Exists(exe + ".old"));
        Assert.False(File.Exists(exe + ".new"));
        Assert.Equal(OldBinary, File.ReadAllBytes(exe));
    }

    [Fact]
    public void Cleanup_without_leftovers_does_nothing()
    {
        using var temp = new TempDirectory();

        UpdateService.CleanupLeftovers(RunningExe(temp));
    }

    [Fact]
    public void Sibling_keeps_the_directory_and_full_name()
    {
        Assert.Equal(@"C:\Tools\portal.exe.old", ExecutableFiles.Sibling(@"C:\Tools\portal.exe", ".old"));
        Assert.Equal(@"C:\Tools\portal.exe.new", ExecutableFiles.Sibling(@"C:\Tools\portal.exe", ".new"));
    }

    [Fact]
    public void Restart_relaunches_the_executable_with_the_original_arguments()
    {
        using var service = new UpdateService(new FakeHttpHandler(), @"C:\Tools\portal.exe", ["--server", "my box"]);

        var startInfo = service.CreateRestartStartInfo();

        Assert.Equal(@"C:\Tools\portal.exe", startInfo.FileName);
        Assert.Equal(["--server", "my box"], startInfo.ArgumentList);
        Assert.False(startInfo.UseShellExecute);
    }

    [LiveFact]
    public async Task Reads_the_live_release_feed()
    {
        using var service = new UpdateService();

        var release = await service.FetchLatestAsync(CancellationToken.None);

        Assert.NotNull(release);
        var info = UpdatePolicy.SelectUpdate(release, "0.0.1");
        Assert.NotNull(info);
        Assert.EndsWith("/portal.exe", info.DownloadUrl, StringComparison.Ordinal);
        Assert.True(info.Size > 0);
    }

    [LiveFact]
    public async Task Reports_a_live_check()
    {
        using var service = new UpdateService();

        var result = await service.CheckAsync("0.0.1");

        Assert.True(result is UpdateCheckResult.Available or UpdateCheckResult.UpToDate, $"unexpected result: {result}");
    }

    [LiveFact]
    public async Task Downloads_and_swaps_the_live_binary()
    {
        using var temp = new TempDirectory();
        var exe = RunningExe(temp);
        using var service = new UpdateService(null, exe, []);
        var check = await service.CheckAsync("0.0.1");
        var info = Assert.IsType<UpdateCheckResult.Available>(check).Release;

        await service.DownloadAndStageAsync(info);

        Assert.Equal(info.Size, new FileInfo(exe).Length);
        Assert.Equal(OldBinary, File.ReadAllBytes(exe + ".old"));
        Assert.False(File.Exists(exe + ".new"));
    }

    private static UpdateService Service(FakeHttpHandler handler, string? exe = null) =>
        new(handler, exe ?? @"C:\Tools\portal.exe", []);

    private static string RunningExe(TempDirectory temp)
    {
        var exe = temp.Combine("portal.exe");
        File.WriteAllBytes(exe, OldBinary);
        return exe;
    }

    private static void AssertUntouched(string exe)
    {
        Assert.Equal(OldBinary, File.ReadAllBytes(exe));
        Assert.False(File.Exists(exe + ".new"));
        Assert.False(File.Exists(exe + ".old"));
    }

    private static byte[] NewBinary(int size)
    {
        var bytes = new byte[size];
        Random.Shared.NextBytes(bytes);
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        return bytes;
    }

    private static ReleaseInfo Info(long size) =>
        new("0.5.0", "https://git.ossalali.com/oss/Portal/releases/tag/0.5.0", DownloadUrl, size);

    private static string Feed(string tag, long size) => $$"""
        {
            "id": 7,
            "tag_name": "{{tag}}",
            "html_url": "https://git.ossalali.com/oss/Portal/releases/tag/{{tag}}",
            "draft": false,
            "prerelease": false,
            "assets": [{
                "name": "portal.exe",
                "browser_download_url": "{{DownloadUrl}}",
                "size": {{size}}
            }]
        }
        """;

    private sealed class RecordingProgress : IProgress<double>
    {
        public List<double> Values { get; } = [];

        public void Report(double value) => Values.Add(value);
    }
}
