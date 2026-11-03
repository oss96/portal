using Portal.Core.Files;

namespace Portal.Core.Connections;

/// <summary>An open connection to one server, as the UI sees it.</summary>
public interface IServerSession : IAsyncDisposable
{
    /// <summary>The saved server this session belongs to.</summary>
    ServerProfile Profile { get; }

    /// <summary>The address actually connected to.</summary>
    string HostName { get; }

    /// <summary>The port connected to.</summary>
    int Port { get; }

    /// <summary>The account signed in as.</summary>
    string User { get; }

    /// <summary>Round-trip time of one request, measured when connecting.</summary>
    TimeSpan Latency { get; }

    /// <summary>File system used for browsing and file operations.</summary>
    IServerFileSystem Files { get; }

    /// <summary>File system on a separate channel, used for transfers.</summary>
    IServerFileSystem TransferFiles { get; }

    /// <summary>True while the connection is up.</summary>
    bool IsConnected { get; }

    /// <summary>Raised once when the connection drops.</summary>
    event EventHandler? ConnectionLost;

    /// <summary>Opens the connection again with the same credentials.</summary>
    Task ReconnectAsync(CancellationToken ct);
}
