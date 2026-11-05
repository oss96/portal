namespace Portal.Core.Files;

/// <summary>POSIX path helpers for server paths.</summary>
public static class RemotePath
{
    /// <summary>Joins a folder and a name.</summary>
    public static string Combine(string directory, string name) =>
        directory.EndsWith('/') ? directory + name : directory + "/" + name;

    /// <summary>The containing folder, or null at "/".</summary>
    public static string? Parent(string path)
    {
        var p = Normalize(path);
        if (p == "/") return null;
        var i = p.LastIndexOf('/');
        return i <= 0 ? "/" : p[..i];
    }

    /// <summary>The last segment of a path.</summary>
    public static string NameOf(string path)
    {
        var p = Normalize(path);
        return p == "/" ? "/" : p[(p.LastIndexOf('/') + 1)..];
    }

    /// <summary>Collapses repeated slashes, "." and ".." segments, and drops a trailing slash.</summary>
    public static string Normalize(string path)
    {
        var parts = new List<string>();
        foreach (var seg in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (seg == ".") continue;
            if (seg == "..")
            {
                if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                continue;
            }
            parts.Add(seg);
        }
        return "/" + string.Join('/', parts);
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="ancestor"/> or lies inside it.</summary>
    public static bool IsWithin(string path, string ancestor)
    {
        var p = Normalize(path);
        var a = Normalize(ancestor);
        return p == a || a == "/" || p.StartsWith(a + "/", StringComparison.Ordinal);
    }
}
