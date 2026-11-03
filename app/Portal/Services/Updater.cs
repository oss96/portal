using System.Reflection;

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

/// <summary>Connects the app to Portal.Core's updater.</summary>
public static partial class Updater
{
    /// <summary>Version of the running build, such as "1.0.0".</summary>
    public static string CurrentVersion { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
}
