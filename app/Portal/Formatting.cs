using System.Globalization;

namespace Portal;

/// <summary>Human-readable sizes, dates and durations.</summary>
public static class Formatting
{
    private const double Kb = 1024, Mb = Kb * 1024, Gb = Mb * 1024, Tb = Gb * 1024;

    /// <summary>"512 B", "12 KB", "4.2 MB", "18.4 GB".</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{Math.Round(bytes / Kb)} KB",
        < 1024L * 1024 * 1024 => (bytes / Mb).ToString(bytes < 10 * Mb ? "0.0" : "0", CultureInfo.CurrentCulture) + " MB",
        < 1024L * 1024 * 1024 * 1024 => (bytes / Gb).ToString("0.0", CultureInfo.CurrentCulture) + " GB",
        _ => (bytes / Tb).ToString("0.0", CultureInfo.CurrentCulture) + " TB",
    };

    /// <summary>"Today, 09:12", "Yesterday, 18:40", "2 Oct", "2 Oct 2025".</summary>
    public static string Date(DateTime utc)
    {
        if (utc <= DateTime.UnixEpoch) return "";
        var local = utc.ToLocalTime();
        var today = DateTime.Today;
        var time = local.ToString("t", CultureInfo.CurrentCulture);
        if (local.Date == today) return $"Today, {time}";
        if (local.Date == today.AddDays(-1)) return $"Yesterday, {time}";
        return local.Year == today.Year
            ? local.ToString("d MMM", CultureInfo.CurrentCulture)
            : local.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
    }

    /// <summary>Full date and time for tooltips and the details pane.</summary>
    public static string FullDate(DateTime utc) =>
        utc <= DateTime.UnixEpoch ? "" : utc.ToLocalTime().ToString("f", CultureInfo.CurrentCulture);

    /// <summary>"12 s left", "3 min left", "1 h 4 min left".</summary>
    public static string TimeLeft(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) return "";
        if (seconds < 60) return $"{Math.Max(1, Math.Round(seconds))} s left";
        if (seconds < 3600) return $"{Math.Round(seconds / 60)} min left";
        return $"{Math.Floor(seconds / 3600)} h {Math.Round(seconds % 3600 / 60)} min left";
    }

    /// <summary>"1 item", "4 items".</summary>
    public static string Count(int n, string singular, string? plural = null) => n == 1 ? $"1 {singular}" : $"{n} {plural ?? singular + "s"}";
}
