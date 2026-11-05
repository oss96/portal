namespace Portal.Core.Connections;

/// <summary>Space on one mounted file system of the server.</summary>
public sealed record DiskUsage(string MountPoint, long TotalBytes, long UsedBytes, long FreeBytes)
{
    /// <summary>Share of the space in use, 0 to 1.</summary>
    public double UsedFraction => TotalBytes <= 0 ? 0 : (double)UsedBytes / TotalBytes;

    private static readonly string[] SkippedTypes = ["tmpfs", "devtmpfs", "overlay", "shm", "efivarfs", "udev", "none", "rootfs", "cgroup", "proc", "sysfs"];
    private static readonly string[] SkippedMounts = ["/dev", "/run", "/sys", "/proc", "/var/lib/docker", "/etc", "/snap", "/usr"];

    /// <summary>Shell command whose output <see cref="ParseDf"/> reads.</summary>
    public const string Command = "df -Pk 2>/dev/null";

    /// <summary>Parses POSIX <c>df -Pk</c> output, keeping real storage of at least 1 GB.</summary>
    public static IReadOnlyList<DiskUsage> ParseDf(string output)
    {
        var list = new List<DiskUsage>();
        foreach (var raw in output.Split('\n').Skip(1))
        {
            var f = raw.TrimEnd('\r').Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 6) continue;
            var source = f[0];
            var mount = string.Join(' ', f[5..]);
            if (SkippedTypes.Contains(source) || SkippedMounts.Any(m => mount == m || mount.StartsWith(m + "/", StringComparison.Ordinal))) continue;
            if (!long.TryParse(f[1], out var total) || !long.TryParse(f[2], out var used) || !long.TryParse(f[3], out var free)) continue;
            if (total * 1024L < 1024L * 1024 * 1024) continue;
            if (list.Any(d => d.MountPoint == mount)) continue;
            list.Add(new DiskUsage(mount, total * 1024L, used * 1024L, free * 1024L));
        }
        return list;
    }
}
