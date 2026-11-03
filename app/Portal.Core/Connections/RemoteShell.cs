using Renci.SshNet;

namespace Portal.Core.Connections;

/// <summary>Output of one command run on the server.</summary>
public sealed record CommandResult(int ExitCode, string Output, string Error)
{
    /// <summary>True when the command exited with status 0.</summary>
    public bool Succeeded => ExitCode == 0;
}

/// <summary>Runs commands on the server over an SSH exec channel.</summary>
public sealed class RemoteShell
{
    private readonly SshClient? _client;

    /// <summary>A shell over an open SSH client.</summary>
    public RemoteShell(SshClient client) => _client = client;

    private RemoteShell(bool sftpOnly)
    {
        _client = null;
        IsSftpOnly = sftpOnly;
    }

    /// <summary>The shell of an account the server limits to SFTP.</summary>
    public static RemoteShell SftpOnly { get; } = new(sftpOnly: true);

    /// <summary>The shell of an account whose command channel didn't open or answer.</summary>
    public static RemoteShell Unreachable { get; } = new(sftpOnly: false);

    /// <summary>False when server-side commands can't run.</summary>
    public bool IsAvailable => _client is not null;

    /// <summary>True when the server itself limits the account to SFTP, as opposed to the channel failing.</summary>
    public bool IsSftpOnly { get; }

    /// <summary>Runs a POSIX sh script that the caller built with <c>PosixShell.Quote</c>. The script reaches <c>sh -s</c> on standard input, so the account's login shell (fish, tcsh, …) only ever parses "sh -s".</summary>
    public async Task<CommandResult> RunAsync(string script, CancellationToken ct)
    {
        if (_client is null) throw new ShellUnavailableException();
        using var cmd = _client.CreateCommand("sh -s");
        cmd.CommandTimeout = TimeSpan.FromHours(6);
        var execution = cmd.ExecuteAsync(ct);
        await using (var input = cmd.CreateInputStream())
            await input.WriteAsync(System.Text.Encoding.UTF8.GetBytes(script + "\n"), ct).ConfigureAwait(false);
        await execution.ConfigureAwait(false);
        return new CommandResult(cmd.ExitStatus ?? -1, cmd.Result, cmd.Error);
    }

    /// <summary>Runs a command and throws <see cref="RemoteCommandException"/> when it fails.</summary>
    public async Task<string> RunCheckedAsync(string commandLine, CancellationToken ct)
    {
        var r = await RunAsync(commandLine, ct).ConfigureAwait(false);
        if (!r.Succeeded) throw new RemoteCommandException(r);
        return r.Output;
    }
}

/// <summary>A server command exited with a non-zero status.</summary>
public sealed class RemoteCommandException(CommandResult result)
    : Exception(string.IsNullOrWhiteSpace(result.Error) ? $"The server reported an error (exit code {result.ExitCode})." : result.Error.Trim())
{
    /// <summary>The failed command's output.</summary>
    public CommandResult Result { get; } = result;
}

/// <summary>The operation needs commands on the server, which this connection can't run.</summary>
public sealed class ShellUnavailableException()
    : NotSupportedException("This needs commands on the server, and they aren’t available on this connection.");

/// <summary>A delete, rename, move or replace would touch a symbolic link, which SFTP follows to what it points to.</summary>
public sealed class SymlinkRefusedException(string path)
    : NotSupportedException($"“{RemoteName(path)}” is a symbolic link. Over SFTP, Portal would change what it points to instead of the link, so it left everything as it was.")
{
    /// <summary>The link's path.</summary>
    public string Path { get; } = path;

    private static string RemoteName(string path) => path.TrimEnd('/')[(path.TrimEnd('/').LastIndexOf('/') + 1)..];
}
