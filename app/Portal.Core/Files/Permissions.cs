namespace Portal.Core.Files;

/// <summary>Formats and parses Unix permission bits.</summary>
public static class Permissions
{
    private static readonly string[] Who = ["Owner", "Group", "Everyone"];

    /// <summary>Nine-character form such as "rwxr-xr-x", with s/t for setuid, setgid and sticky.</summary>
    public static string ToSymbolic(int mode)
    {
        Span<char> c = stackalloc char[9];
        for (var i = 0; i < 3; i++)
        {
            var bits = (mode >> ((2 - i) * 3)) & 7;
            c[i * 3] = (bits & 4) != 0 ? 'r' : '-';
            c[i * 3 + 1] = (bits & 2) != 0 ? 'w' : '-';
            c[i * 3 + 2] = (bits & 1) != 0 ? 'x' : '-';
        }
        if ((mode & 0x800) != 0) c[2] = c[2] == 'x' ? 's' : 'S';
        if ((mode & 0x400) != 0) c[5] = c[5] == 'x' ? 's' : 'S';
        if ((mode & 0x200) != 0) c[8] = c[8] == 'x' ? 't' : 'T';
        return new string(c);
    }

    /// <summary>Octal form as chmod takes it, such as "755" or "4755".</summary>
    public static string ToOctal(int mode)
    {
        var special = (mode >> 9) & 7;
        var basic = Convert.ToString(mode & 0x1FF, 8).PadLeft(3, '0');
        return special == 0 ? basic : special + basic;
    }

    /// <summary>Parses an octal string such as "644" or "0755".</summary>
    public static bool TryParseOctal(string text, out int mode)
    {
        mode = 0;
        text = text.Trim();
        if (text.Length is < 3 or > 4 || text.Any(ch => ch is < '0' or > '7')) return false;
        mode = Convert.ToInt32(text, 8);
        return true;
    }

    /// <summary>Plain-language lines such as "Owner can read, write, run".</summary>
    public static IReadOnlyList<string> Describe(int mode)
    {
        var lines = new string[3];
        for (var i = 0; i < 3; i++)
        {
            var bits = (mode >> ((2 - i) * 3)) & 7;
            var verbs = new List<string>(3);
            if ((bits & 4) != 0) verbs.Add("read");
            if ((bits & 2) != 0) verbs.Add("write");
            if ((bits & 1) != 0) verbs.Add("run");
            lines[i] = verbs.Count == 0 ? $"{Who[i]} has no access" : $"{Who[i]} can {string.Join(", ", verbs)}";
        }
        return lines;
    }
}
