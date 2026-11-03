using System.Net.Sockets;
using Renci.SshNet.Common;

namespace Portal.Core.Connections;

/// <summary>The server presented a different host key than the one saved in known_hosts.</summary>
public sealed class HostKeyChangedException(HostKeyInfo key)
    : Exception($"The identity of {key.Host} changed since the last connection.")
{
    /// <summary>The key the server presented.</summary>
    public HostKeyInfo Key { get; } = key;
}

/// <summary>The user declined a prompt during connect.</summary>
public sealed class ConnectCancelledException() : OperationCanceledException("Connecting was cancelled.");

/// <summary>Turns exceptions into messages a person can act on.</summary>
public static class FriendlyError
{
    /// <summary>A short title and a sentence of advice for a failed connect.</summary>
    public static (string Title, string Detail) ForConnect(Exception e, string host, int port, string user)
    {
        var inner = Unwrap(e);
        return inner switch
        {
            HostKeyChangedException h => ($"{host}’s identity changed",
                $"The server now presents {h.Key.KeyType} key {h.Key.Fingerprint}, which doesn’t match the one saved in {h.Key.KnownHostsPath}. If the server was reinstalled, remove its old line from that file and connect again. Otherwise, don’t connect."),
            SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain } =>
                ($"Couldn’t find “{host}”", "Check the address, or make sure this PC is on the same network or connected to Tailscale."),
            SocketException { SocketErrorCode: SocketError.ConnectionRefused } =>
                ($"{host} refused the connection", $"Nothing is accepting SSH connections on port {port}. Check the port and that the SSH service is running."),
            SocketException { SocketErrorCode: SocketError.TimedOut or SocketError.HostUnreachable or SocketError.NetworkUnreachable } or SshOperationTimeoutException or TimeoutException =>
                ($"{host} didn’t answer", "The server may be off, or a firewall is in the way. Check that you can reach it from this PC."),
            SshAuthenticationException => ($"{host} didn’t accept the sign-in",
                $"The server rejected the key or password for “{user}”. Check the user name, and that your public key is in ~/.ssh/authorized_keys on the server."),
            SshPassPhraseNullOrEmptyException => ("The key needs its passphrase", "Enter the passphrase for your SSH key to sign in."),
            ConnectCancelledException => ("Cancelled", "Connecting was cancelled."),
            SshConnectionException => ($"Lost the connection to {host}", inner.Message),
            _ => ($"Couldn’t connect to {host}", inner.Message),
        };
    }

    /// <summary>One sentence for a failed file operation.</summary>
    public static string ForOperation(Exception e)
    {
        var inner = Unwrap(e);
        return inner switch
        {
            SftpPermissionDeniedException or UnauthorizedAccessException => "Permission denied. The account you signed in with isn’t allowed to do this.",
            SftpPathNotFoundException or FileNotFoundException or DirectoryNotFoundException => "It doesn’t exist any more. Refresh to see the current state.",
            SshConnectionException or ObjectDisposedException => "The connection to the server dropped.",
            IOException io when io.HResult == unchecked((int)0x80070070) => "There isn’t enough free space.",
            _ => inner.Message,
        };
    }

    private static Exception Unwrap(Exception e)
    {
        while (e is AggregateException { InnerException: { } a }) e = a;
        if (e is SshConnectionException { InnerException: SocketException s }) return s;
        return e;
    }
}
