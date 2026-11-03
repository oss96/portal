namespace Portal.Core.Connections;

/// <summary>A server's host key as presented during connect.</summary>
public sealed record HostKeyInfo(string Host, int Port, string KeyType, string Fingerprint, string KnownHostsPath);

/// <summary>Steps of connecting, in order.</summary>
public enum ConnectStage
{
    /// <summary>Reaching the server.</summary>
    Reaching,
    /// <summary>Checking the server's host key.</summary>
    VerifyingIdentity,
    /// <summary>Signing in.</summary>
    SigningIn,
    /// <summary>Opening SFTP and the command channel.</summary>
    OpeningFiles,
}

/// <summary>Questions the connect flow asks the user.</summary>
public interface IConnectPrompts
{
    /// <summary>Asks whether to trust a server Portal has never seen; true saves the key and continues.</summary>
    Task<bool> TrustNewHostAsync(HostKeyInfo key);

    /// <summary>Asks for the passphrase of an encrypted key; null cancels.</summary>
    Task<string?> AskPassphraseAsync(string keyPath, bool retry);

    /// <summary>Asks for the account password; null cancels.</summary>
    Task<string?> AskPasswordAsync(string user, string host, bool retry);
}
