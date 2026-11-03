namespace Portal.Core.Connections;

/// <summary>Maps numeric user and group ids on the server to their names.</summary>
public sealed class AccountNames
{
    /// <summary>Separator line between the passwd and group parts of <see cref="ReadCommand"/>'s output.</summary>
    public const string Separator = "--portal-groups--";

    /// <summary>Shell command that prints the passwd database, the separator, then the group database.</summary>
    public static readonly string ReadCommand =
        $"(getent passwd || cat /etc/passwd) 2>/dev/null; echo '{Separator}'; (getent group || cat /etc/group) 2>/dev/null";

    private readonly Dictionary<long, string> _users = [];
    private readonly Dictionary<long, string> _groups = [];

    /// <summary>An empty map that shows numeric ids.</summary>
    public static AccountNames Empty => new();

    /// <summary>Parses the output of <see cref="ReadCommand"/>.</summary>
    public static AccountNames Parse(string output)
    {
        var names = new AccountNames();
        var target = names._users;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line == Separator) { target = names._groups; continue; }
            var f = line.Split(':');
            if (f.Length >= 3 && long.TryParse(f[2], out var id) && f[0].Length > 0) target.TryAdd(id, f[0]);
        }
        return names;
    }

    /// <summary>User name for a uid, or the uid itself.</summary>
    public string User(long id) => _users.TryGetValue(id, out var n) ? n : id.ToString();

    /// <summary>Group name for a gid, or the gid itself.</summary>
    public string Group(long id) => _groups.TryGetValue(id, out var n) ? n : id.ToString();

    /// <summary>All user names, sorted.</summary>
    public IReadOnlyList<string> UserNames => [.. _users.Values.Distinct().Order(StringComparer.Ordinal)];

    /// <summary>All group names, sorted.</summary>
    public IReadOnlyList<string> GroupNames => [.. _groups.Values.Distinct().Order(StringComparer.Ordinal)];
}
