using System.Globalization;
using System.Text;

namespace Portal.Core.Ssh;

/// <summary>An OpenSSH client configuration such as <c>~/.ssh/config</c>, read with OpenSSH's rules.</summary>
public sealed class SshConfig
{
    private const int MaxIncludeDepth = 16;

    private readonly IReadOnlyList<ConfigEntry> entries;
    private readonly string homeDirectory;

    private SshConfig(IReadOnlyList<ConfigEntry> entries, IReadOnlyList<string> hostAliases, string homeDirectory)
    {
        this.entries = entries;
        this.homeDirectory = homeDirectory;
        HostAliases = hostAliases;
    }

    /// <summary>Concrete names from <c>Host</c> lines, without wildcards or negations, in file order.</summary>
    public IReadOnlyList<string> HostAliases { get; }

    /// <summary>Reads the config at <paramref name="path"/> for the current Windows user; a missing file gives an empty config.</summary>
    public static SshConfig Load(string path) =>
        Load(path, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary>Reads the config at <paramref name="path"/>, expanding <c>~</c> to <paramref name="homeDirectory"/>.</summary>
    public static SshConfig Load(string path, string homeDirectory) =>
        File.Exists(path) ? Parse(File.ReadAllText(path), homeDirectory) : Parse(string.Empty, homeDirectory);

    /// <summary>Parses config text; relative <c>Include</c> paths resolve against <c>.ssh</c> in <paramref name="homeDirectory"/>.</summary>
    public static SshConfig Parse(string text, string homeDirectory)
    {
        var builder = new Builder(homeDirectory);
        builder.Read(text, [], 0);
        return builder.Build();
    }

    /// <summary>Applies every matching block to <paramref name="host"/>; the first value of each keyword wins.</summary>
    public SshHostSettings Resolve(string host)
    {
        string? hostName = null;
        string? user = null;
        int? port = null;
        var identityFiles = new List<string>();

        foreach (var entry in entries.Where(e => e.AppliesTo(host)))
        {
            var value = entry.Arguments[0];
            switch (entry.Keyword)
            {
                case "hostname":
                    hostName ??= value;
                    break;
                case "user":
                    user ??= value;
                    break;
                case "port":
                    port ??= ParsePort(value);
                    break;
                case "identityfile":
                    identityFiles.Add(value);
                    break;
            }
        }

        var resolvedHostName = hostName is null ? host : ExpandTokens(hostName, ('h', host));
        var expandedIdentities = identityFiles
            .Where(file => !file.Equals("none", StringComparison.OrdinalIgnoreCase))
            .Select(file => ExpandTokens(ExpandTilde(file, homeDirectory), ('d', homeDirectory), ('h', resolvedHostName)))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new SshHostSettings(host, resolvedHostName, user, port, expandedIdentities);
    }

    internal static List<string>? SplitArguments(string text)
    {
        var arguments = new List<string>();
        var index = 0;
        while (true)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            if (index >= text.Length || text[index] == '#')
            {
                return arguments;
            }

            var current = new StringBuilder();
            var quote = '\0';
            for (; index < text.Length; index++)
            {
                var c = text[index];
                if (quote == '\0' && char.IsWhiteSpace(c))
                {
                    break;
                }

                if (c == '\\' && index + 1 < text.Length && IsEscapable(text[index + 1], quote))
                {
                    current.Append(text[++index]);
                }
                else if (quote == '\0' && c is '"' or '\'')
                {
                    quote = c;
                }
                else if (c == quote)
                {
                    quote = '\0';
                }
                else
                {
                    current.Append(c);
                }
            }

            if (quote != '\0')
            {
                return null;
            }

            arguments.Add(current.ToString());
        }
    }

    private static bool IsEscapable(char next, char quote) =>
        next is '"' or '\'' or '\\' ||(quote == '\0' && next == ' ');

    private static bool TryParseLine(string line, out string keyword, out List<string> arguments)
    {
        keyword = string.Empty;
        arguments = [];
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed[0] == '#')
        {
            return false;
        }

        var end = 0;
        while (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end]) && trimmed[end] != '=')
        {
            end++;
        }

        var rest = trimmed[end..].TrimStart();
        if (rest.StartsWith('='))
        {
            rest = rest[1..];
        }

        var split = SplitArguments(rest);
        if (end == 0 || split is null)
        {
            return false;
        }

        keyword = trimmed[..end].ToLowerInvariant();
        arguments = split;
        return true;
    }

    private static int? ParsePort(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is > 0 and <= 65535
            ? port
            : null;

    private static string ExpandTilde(string path, string homeDirectory) =>
        path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith(@"~\", StringComparison.Ordinal)
            ? homeDirectory + path[1..]
            : path;

    private static string ExpandTokens(string value, params (char Token, string Replacement)[] tokens)
    {
        var result = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '%' || i + 1 >= value.Length)
            {
                result.Append(value[i]);
                continue;
            }

            var token = value[i + 1];
            var match = tokens.FirstOrDefault(t => t.Token == token);
            if (token == '%')
            {
                result.Append('%');
                i++;
            }
            else if (match.Replacement is not null)
            {
                result.Append(match.Replacement);
                i++;
            }
            else
            {
                result.Append('%');
            }
        }

        return result.ToString();
    }

    private sealed record ConfigEntry(IReadOnlyList<IReadOnlyList<string>> Conditions, string Keyword, IReadOnlyList<string> Arguments)
    {
        public bool AppliesTo(string host) => Conditions.All(patterns => HostPatterns.MatchesList(host, patterns));
    }

    private sealed class Builder(string homeDirectory)
    {
        private readonly List<ConfigEntry> entries = [];
        private readonly List<string> aliases = [];
        private readonly HashSet<string> seenAliases = new(StringComparer.Ordinal);

        public SshConfig Build() => new(entries, aliases, homeDirectory);

        public void Read(string text, IReadOnlyList<IReadOnlyList<string>> outerConditions, int depth)
        {
            var conditions = outerConditions;
            var insideMatch = false;
            using var reader = new StringReader(text);
            while (reader.ReadLine() is { } line)
            {
                if (!TryParseLine(line, out var keyword, out var arguments))
                {
                    continue;
                }

                switch (keyword)
                {
                    case "host":
                        insideMatch = false;
                        conditions = [.. outerConditions, arguments];
                        RecordAliases(arguments);
                        break;
                    case "match":
                        insideMatch = true;
                        break;
                    case "include" when !insideMatch:
                        Include(arguments, conditions, depth);
                        break;
                    default:
                        if (!insideMatch && arguments.Count > 0)
                        {
                            entries.Add(new ConfigEntry(conditions, keyword, arguments));
                        }

                        break;
                }
            }
        }

        private void RecordAliases(IEnumerable<string> patterns)
        {
            foreach (var pattern in patterns.Where(p => !p.StartsWith('!') && !HostPatterns.HasWildcard(p)))
            {
                if (seenAliases.Add(pattern))
                {
                    aliases.Add(pattern);
                }
            }
        }

        private void Include(IEnumerable<string> paths, IReadOnlyList<IReadOnlyList<string>> conditions, int depth)
        {
            if (depth >= MaxIncludeDepth)
            {
                return;
            }

            foreach (var file in paths.SelectMany(path => ExpandGlob(ToAbsolute(path))))
            {
                string text;
                try
                {
                    text = File.ReadAllText(file);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                Read(text, conditions, depth + 1);
            }
        }

        private string ToAbsolute(string path)
        {
            var expanded = ExpandTilde(path, homeDirectory);
            return Path.IsPathRooted(expanded) ? expanded : Path.Combine(homeDirectory, ".ssh", expanded);
        }

        private static IEnumerable<string> ExpandGlob(string path)
        {
            if (!HostPatterns.HasWildcard(path))
            {
                return File.Exists(path) ? [path] : [];
            }

            var root = Path.GetPathRoot(path) ?? string.Empty;
            var segments = path[root.Length..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
            IEnumerable<string> current = [root];
            for (var i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                var isLast = i == segments.Length - 1;
                current = current
                    .SelectMany(directory => MatchSegment(directory, segment, isLast))
                    .Order(StringComparer.Ordinal)
                    .ToList();
            }

            return current;
        }

        private static IEnumerable<string> MatchSegment(string directory, string segment, bool isLast)
        {
            if (!HostPatterns.HasWildcard(segment))
            {
                var candidate = Path.Combine(directory, segment);
                var exists = isLast ? File.Exists(candidate) : Directory.Exists(candidate);
                return exists ? [candidate] : [];
            }

            if (!Directory.Exists(directory))
            {
                return [];
            }

            var children = isLast ? Directory.EnumerateFiles(directory) : Directory.EnumerateDirectories(directory);
            return children.Where(child => HostPatterns.Matches(Path.GetFileName(child), segment)).ToList();
        }
    }
}
