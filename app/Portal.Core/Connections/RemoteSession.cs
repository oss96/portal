using Portal.Core.Files;
using Renci.SshNet;

namespace Portal.Core.Connections;

/// <summary>An open connection to one server: a browsing SFTP channel, a transfer SFTP channel and a command channel.</summary>
public sealed class RemoteSession : IAsyncDisposable
{
    private readonly SftpClient _browse;
    private readonly SftpClient _transfer;
    private readonly SshClient _shell;
    private int _lost;

    internal RemoteSession(ServerProfile profile, string hostName, int port, string user, SftpClient browse, SftpClient transfer, SshClient shell, AccountNames names, TimeSpan latency)
    {
        Profile = profile;
        HostName = hostName;
        Port = port;
        User = user;
        _browse = browse;
        _transfer = transfer;
        _shell = shell;
        Latency = latency;
        var remoteShell = new RemoteShell(shell);
        Files = new RemoteFileSystem(profile.Name, browse, remoteShell, names);
        TransferFiles = new RemoteFileSystem(profile.Name, transfer, remoteShell, names);
        foreach (var c in new BaseClient[] { browse, transfer, shell }) c.ErrorOccurred += (_, _) => OnLost();
    }

    /// <summary>The saved server this session belongs to.</summary>
    public ServerProfile Profile { get; }

    /// <summary>The address actually connected to, after SSH config lookup.</summary>
    public string HostName { get; }

    /// <summary>The port connected to.</summary>
    public int Port { get; }

    /// <summary>The account signed in as.</summary>
    public string User { get; }

    /// <summary>Round-trip time of one SFTP request, measured when connecting.</summary>
    public TimeSpan Latency { get; private set; }

    /// <summary>File system used for browsing and file operations.</summary>
    public RemoteFileSystem Files { get; }

    /// <summary>File system on a separate channel, used for transfers so they don't slow browsing down.</summary>
    public RemoteFileSystem TransferFiles { get; }

    /// <summary>True while all three channels are up.</summary>
    public bool IsConnected => _browse.IsConnected && _transfer.IsConnected && _shell.IsConnected;

    /// <summary>Raised once when a channel drops.</summary>
    public event EventHandler? ConnectionLost;

    /// <summary>Opens dropped channels again with the same credentials.</summary>
    public async Task ReconnectAsync(CancellationToken ct)
    {
        foreach (var c in new BaseClient[] { _browse, _transfer, _shell })
        {
            if (c.IsConnected) continue;
            try { c.Disconnect(); } catch (Exception) { }
            await c.ConnectAsync(ct).ConfigureAwait(false);
        }
        Interlocked.Exchange(ref _lost, 0);
        Latency = await SshConnector.MeasureLatencyAsync(_browse, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        foreach (var c in new BaseClient[] { _browse, _transfer, _shell })
        {
            try { if (c.IsConnected) c.Disconnect(); } catch (Exception) { }
            c.Dispose();
        }
        return ValueTask.CompletedTask;
    }

    private void OnLost()
    {
        if (Interlocked.Exchange(ref _lost, 1) == 0) ConnectionLost?.Invoke(this, EventArgs.Empty);
    }
}
