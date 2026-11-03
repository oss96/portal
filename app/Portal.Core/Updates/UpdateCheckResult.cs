namespace Portal.Core.Updates;

/// <summary>The outcome of asking Forgejo for a newer release.</summary>
public abstract record UpdateCheckResult
{
    private UpdateCheckResult()
    {
    }

    /// <summary>No newer stable release exists.</summary>
    public sealed record UpToDate : UpdateCheckResult;

    /// <summary>A newer stable release can be downloaded.</summary>
    public sealed record Available(ReleaseInfo Release) : UpdateCheckResult;

    /// <summary>The check failed; <see cref="Message"/> says why.</summary>
    public sealed record Error(string Message) : UpdateCheckResult;
}
