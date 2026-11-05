using System.Reflection;
using System.Windows;
using Portal.Core.Updates;

namespace Portal.Services;

/// <summary>Result of asking the release feed for a newer version.</summary>
public abstract record UpdateCheck
{
    /// <summary>Nothing newer exists.</summary>
    public sealed record UpToDate : UpdateCheck;

    /// <summary>A newer release can be installed.</summary>
    public sealed record Available(string Version) : UpdateCheck;

    /// <summary>The check failed.</summary>
    public sealed record Failed(string Message) : UpdateCheck;
}

/// <summary>Connects the app to Portal.Core's updater, which installs releases from git.ossalali.com.</summary>
public static class Updater
{
    private static readonly UpdateService Service = new();
    private static ReleaseInfo? _available;

    /// <summary>Version of the running build, such as "1.0.0".</summary>
    public static string CurrentVersion { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    /// <summary>Deletes portal.exe.old and portal.exe.new left by the previous update.</summary>
    public static void CleanupAfterUpdate()
    {
        if (Environment.ProcessPath is { } exe) UpdateService.CleanupLeftovers(exe);
    }

    /// <summary>Asks the release feed for a newer version.</summary>
    public static async Task<UpdateCheck> CheckAsync(CancellationToken ct)
    {
        switch (await Service.CheckAsync(CurrentVersion, ct))
        {
            case UpdateCheckResult.Available a:
                _available = a.Release;
                return new UpdateCheck.Available(a.Release.Version);
            case UpdateCheckResult.Error e:
                return new UpdateCheck.Failed(e.Message);
            default:
                return new UpdateCheck.UpToDate();
        }
    }

    /// <summary>Downloads the release found by <see cref="CheckAsync"/> and swaps it in next to the running exe.</summary>
    public static Task StageAsync(IProgress<double> progress, CancellationToken ct) =>
        _available is { } release ? Service.DownloadAndStageAsync(release, progress, ct) : throw new InvalidOperationException("Check for updates first.");

    /// <summary>Starts the new version and closes this one.</summary>
    public static void Restart()
    {
        Service.Restart();
        Application.Current.Shutdown();
    }
}
