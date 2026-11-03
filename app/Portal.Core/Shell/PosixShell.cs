namespace Portal.Core.Shell;

/// <summary>Builds command lines for a POSIX shell on the remote side.</summary>
public static class PosixShell
{
    /// <summary>Wraps one argument in single quotes so the shell passes it through unchanged.</summary>
    public static string Quote(string arg) => "'" + arg.Replace("'", @"'\''", StringComparison.Ordinal) + "'";

    /// <summary>Quotes every argument and joins them with single spaces.</summary>
    public static string Join(IEnumerable<string> args) => string.Join(' ', args.Select(Quote));
}
