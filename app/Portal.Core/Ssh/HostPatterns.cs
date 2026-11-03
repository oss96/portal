namespace Portal.Core.Ssh;

/// <summary>OpenSSH host pattern matching with <c>*</c>, <c>?</c> and <c>!</c> negation, ignoring case.</summary>
internal static class HostPatterns
{
    /// <summary>True when a pattern in the list matches and no negated pattern does.</summary>
    public static bool MatchesList(string name, IEnumerable<string> patterns)
    {
        var matched = false;
        foreach (var raw in patterns)
        {
            var negated = raw.StartsWith('!');
            var pattern = negated ? raw[1..] : raw;
            if (pattern.Length == 0 || !Matches(name, pattern))
            {
                continue;
            }

            if (negated)
            {
                return false;
            }

            matched = true;
        }

        return matched;
    }

    /// <summary>True when the whole text matches one wildcard pattern.</summary>
    public static bool Matches(string text, string pattern)
    {
        int textIndex = 0, patternIndex = 0, starIndex = -1, starTextIndex = 0;
        while (textIndex < text.Length)
        {
            if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex++;
                starTextIndex = textIndex;
            }
            else if (patternIndex < pattern.Length && (pattern[patternIndex] == '?' || SameLetter(pattern[patternIndex], text[textIndex])))
            {
                textIndex++;
                patternIndex++;
            }
            else if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                textIndex = ++starTextIndex;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    /// <summary>True when the text contains <c>*</c> or <c>?</c>.</summary>
    public static bool HasWildcard(string text) => text.AsSpan().IndexOfAny('*', '?') >= 0;

    private static bool SameLetter(char a, char b) => char.ToLowerInvariant(a) == char.ToLowerInvariant(b);
}
