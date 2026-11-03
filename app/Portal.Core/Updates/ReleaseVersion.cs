using System.Globalization;

namespace Portal.Core.Updates;

/// <summary>A release version of the form <c>X.Y.Z</c>, optionally written with a leading <c>v</c>.</summary>
public readonly record struct ReleaseVersion(ulong Major, ulong Minor, ulong Patch) : IComparable<ReleaseVersion>
{
    /// <summary>Parses <c>X.Y.Z</c> or <c>vX.Y.Z</c>; anything else fails.</summary>
    public static bool TryParse(string text, out ReleaseVersion version)
    {
        version = default;
        var parts = (text.StartsWith('v') ? text[1..] : text).Split('.');
        if (parts.Length != 3
            || !TryParsePart(parts[0], out var major)
            || !TryParsePart(parts[1], out var minor)
            || !TryParsePart(parts[2], out var patch))
        {
            return false;
        }

        version = new ReleaseVersion(major, minor, patch);
        return true;
    }

    /// <summary>True when both parse and <paramref name="candidate"/> is the higher version.</summary>
    public static bool IsNewer(string candidate, string current) =>
        TryParse(candidate, out var candidateVersion)
        && TryParse(current, out var currentVersion)
        && candidateVersion > currentVersion;

    /// <inheritdoc />
    public int CompareTo(ReleaseVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    /// <summary>Compares two versions numerically.</summary>
    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;

    /// <summary>Compares two versions numerically.</summary>
    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;

    /// <summary>Compares two versions numerically.</summary>
    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;

    /// <summary>Compares two versions numerically.</summary>
    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;

    /// <summary>The version as <c>X.Y.Z</c>.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");

    private static bool TryParsePart(string text, out ulong value) =>
        ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
