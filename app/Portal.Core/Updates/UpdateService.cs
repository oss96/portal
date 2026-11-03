using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;

namespace Portal.Core.Updates;

/// <summary>Checks Forgejo for a newer Portal, swaps the downloaded binary in beside the running one, and relaunches.</summary>
public sealed class UpdateService : IDisposable
{
    /// <summary>Deadline for the small release metadata request.</summary>
    public static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A download has no overall deadline; it ends only when no data arrives for this long.</summary>
    public static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

    private const int BufferSize = 81920;

    private static readonly JsonSerializerOptions FeedOptions = new()
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    private readonly HttpClient http;
    private readonly string? executablePath;
    private readonly IReadOnlyList<string> restartArguments;

    /// <summary>Creates a service for the running executable and its command line.</summary>
    public UpdateService()
        : this(null)
    {
    }

    /// <summary>Creates a service with a custom HTTP handler, executable path and restart arguments.</summary>
    /// <param name="handler">Sends the HTTP requests; null uses a socket handler with a connect timeout.</param>
    /// <param name="executablePath">The binary to replace; null uses the running process.</param>
    /// <param name="restartArguments">Arguments for the relaunch; null reuses this process's arguments.</param>
    public UpdateService(HttpMessageHandler? handler, string? executablePath = null, IReadOnlyList<string>? restartArguments = null)
    {
        http = new HttpClient(handler ?? new SocketsHttpHandler { ConnectTimeout = ApiTimeout }, disposeHandler: handler is null)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Portal/{CurrentVersion}");
        this.executablePath = executablePath ?? Environment.ProcessPath;
        this.restartArguments = restartArguments ?? Environment.GetCommandLineArgs().Skip(1).ToList();
    }

    /// <summary>The version compiled into this build, as <c>X.Y.Z</c>.</summary>
    public static string CurrentVersion { get; } = ReadCurrentVersion();

    /// <summary>Asks Forgejo for the latest release and reports whether it is newer than <paramref name="currentVersion"/>.</summary>
    public async Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            var release = await FetchLatestAsync(cancellationToken).ConfigureAwait(false);
            var info = release is null ? null : UpdatePolicy.SelectUpdate(release, currentVersion);
            return info is null ? new UpdateCheckResult.UpToDate() : new UpdateCheckResult.Available(info);
        }
        catch (UpdateException e)
        {
            return new UpdateCheckResult.Error($"Update check failed: {Describe(e)}");
        }
    }

    /// <summary>Downloads the release to <c>.new</c> beside the executable, verifies it and swaps it into place.</summary>
    /// <param name="release">The release from <see cref="CheckAsync"/>.</param>
    /// <param name="progress">Receives the downloaded fraction from 0 to 1.</param>
    /// <param name="cancellationToken">Stops the download and removes the partial file.</param>
    /// <exception cref="UpdateException">The download or the swap failed; the installed binary is unchanged.</exception>
    public async Task DownloadAndStageAsync(ReleaseInfo release, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var exe = executablePath ?? throw new UpdateException("Cannot locate the running executable");

        string staged;
        try
        {
            staged = await DownloadAsync(release, exe, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (UpdateException e)
        {
            throw new UpdateException($"Download failed: {Describe(e)}", e);
        }

        try
        {
            ExecutableFiles.Swap(exe, staged);
        }
        catch (UpdateException e)
        {
            throw new UpdateException($"Install failed: {Describe(e)}", e);
        }
    }

    /// <summary>Starts the swapped executable with the original arguments; the caller then closes this instance.</summary>
    /// <exception cref="UpdateException">The executable could not be started.</exception>
    public void Restart()
    {
        var startInfo = CreateRestartStartInfo();
        try
        {
            using var process = Process.Start(startInfo);
        }
        catch (Win32Exception e)
        {
            throw new UpdateException($"failed to start {startInfo.FileName}", e);
        }
    }

    /// <summary>Removes the <c>.old</c> and <c>.new</c> files an earlier update left beside <paramref name="exePath"/>.</summary>
    public static void CleanupLeftovers(string exePath) => ExecutableFiles.CleanupLeftovers(exePath);

    /// <inheritdoc />
    public void Dispose() => http.Dispose();

    internal ProcessStartInfo CreateRestartStartInfo()
    {
        var exe = executablePath ?? throw new UpdateException("executable path unknown");
        var startInfo = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var argument in restartArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    internal async Task<ReleaseDto?> FetchLatestAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ApiTimeout);
        try
        {
            using var response = await http.GetAsync(UpdatePolicy.LatestReleaseApi, deadline.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateException($"Forgejo rejected the request ({DescribeStatus(response)})");
            }

            return await response.Content.ReadFromJsonAsync<ReleaseDto>(FeedOptions, deadline.Token).ConfigureAwait(false)
                ?? throw new UpdateException("unexpected release feed");
        }
        catch (HttpRequestException e)
        {
            throw new UpdateException("request failed", e);
        }
        catch (JsonException e)
        {
            throw new UpdateException("unexpected release feed", e);
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpdateException("request timed out", e);
        }
    }

    internal async Task<string> DownloadAsync(ReleaseInfo release, string exe, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!UpdatePolicy.IsReleaseHost(release.DownloadUrl))
        {
            throw new UpdateException($"the download points outside {UpdatePolicy.ReleaseHost}");
        }

        if (release.Size <= 0)
        {
            throw new UpdateException($"release {release.Version} reports an empty {UpdatePolicy.AssetName}");
        }

        var target = ExecutableFiles.Sibling(exe, ExecutableFiles.StagedSuffix);
        try
        {
            await StreamToFileAsync(release, target, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is UpdateException or HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException)
        {
            ExecutableFiles.TryDelete(target);
            if (e is UpdateException || (e is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                throw;
            }

            throw new UpdateException(DescribeDownloadFailure(e, target), e);
        }

        if (!StartsWithPeHeader(target))
        {
            ExecutableFiles.TryDelete(target);
            throw new UpdateException("the downloaded file is not a Windows executable");
        }

        progress?.Report(1.0);
        return target;
    }

    private async Task StreamToFileAsync(ReleaseInfo release, string target, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stall.CancelAfter(StallTimeout);
        using var response = await http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, stall.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new UpdateException($"Forgejo rejected the download ({DescribeStatus(response)})");
        }

        var body = await response.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false);
        await using var bodyScope = body.ConfigureAwait(false);
        var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        await using var fileScope = file.ConfigureAwait(false);

        var buffer = new byte[BufferSize];
        var reportStep = Math.Max(release.Size / 100, 1);
        long received = 0;
        long lastReported = 0;
        while (true)
        {
            stall.CancelAfter(StallTimeout);
            var read = await body.ReadAsync(buffer, stall.Token).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            received += read;
            if (received > release.Size)
            {
                throw new UpdateException($"download is larger than the announced {release.Size} bytes");
            }

            await file.WriteAsync(buffer.AsMemory(0, read), stall.Token).ConfigureAwait(false);
            if (received - lastReported >= reportStep)
            {
                lastReported = received;
                progress?.Report((double)received / release.Size);
            }
        }

        if (received != release.Size)
        {
            throw new UpdateException($"download is incomplete: {received} of {release.Size} bytes");
        }
    }

    private static bool StartsWithPeHeader(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[2];
            return stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length
                && header[0] == (byte)'M'
                && header[1] == (byte)'Z';
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string DescribeDownloadFailure(Exception exception, string target) => exception switch
    {
        HttpRequestException or HttpIOException => "connection lost",
        OperationCanceledException => $"no data arrived for {StallTimeout.TotalSeconds:0} seconds",
        _ => $"cannot write {target}",
    };

    private static string DescribeStatus(HttpResponseMessage response) =>
        $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd();

    private static string Describe(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(": ", messages);
    }

    private static string ReadCurrentVersion()
    {
        var assembly = typeof(UpdateService).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var metadata = version.IndexOf('+', StringComparison.Ordinal);
        return metadata < 0 ? version : version[..metadata];
    }
}
