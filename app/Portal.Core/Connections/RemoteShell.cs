using Renci.SshNet;

namespace Portal.Core.Connections;

/// <summary>Output of one command run on the server.</summary>
public sealed record CommandResult(int ExitCode, string Output, string Error)
{
    /// <summary>True when the command exited with status 0.</summary>
    public bool Succeeded => ExitCode == 0;
}

/// <summary>Runs commands on the server over an SSH exec channel.</summary>
public sealed class RemoteShell(SshClient client)
{
    /// <summary>Runs a command line that the caller built with <c>PosixShell.Quote</c>.</summary>
    public async Task<CommandResult> RunAsync(string commandLine, CancellationToken ct)
    {
        using var cmd = client.CreateCommand(commandLine);
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
