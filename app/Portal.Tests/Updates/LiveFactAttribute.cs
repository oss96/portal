namespace Portal.Tests.Updates;

/// <summary>A fact that talks to git.ossalali.com; it runs only when PORTAL_LIVE_TESTS=1.</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    /// <summary>Marks the test skipped unless live tests are switched on.</summary>
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PORTAL_LIVE_TESTS") != "1")
        {
            Skip = "requires network access to git.ossalali.com; set PORTAL_LIVE_TESTS=1";
        }
    }
}
