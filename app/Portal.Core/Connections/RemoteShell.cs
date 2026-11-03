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

    private RemoteShell() => _client = null;

    /// <summary>The shell of an account that may only use SFTP.</summary>
    public static RemoteShell Unavailable { get; } = new();

    /// <summary>False for accounts limited to SFTP, where server-side commands can't run.</summary>
    public bool IsAvailable => _client is not null;

    /// <summary>Runs a command line that the caller built with <c>PosixShell.Quote</c>.</summary>
    public async Task<CommandResult> RunAsync(string commandLine, CancellationToken ct)
    {
        if (_client is null) throw new ShellUnavailableException();
        using var cmd = _client.CreateCommand(commandLine);
        cmd.CommandTimeout = TimeSpan.FromHours(6);
        await cmd.ExecuteAsync(ct).ConfigureAwait(false);
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

/// <summary>The account may only use SFTP, so a server-side command can't run.</summary>
public sealed class ShellUnavailableException()
    : NotSupportedException("This account may only transfer files (SFTP only), so Portal can’t run this on the server.");

/// <summary>A delete, rename or move would touch a symbolic link, which SFTP follows to its target.</summary>
public sealed class SymlinkDeleteException(string path)
    : NotSupportedException($"“{path}” is a symbolic link. This account may only use SFTP, which would act on what the link points to, so Portal left everything as it was.");
