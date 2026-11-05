namespace Portal.Core.Connections;

/// <summary>How Portal signs in to a server.</summary>
public enum AuthKind
{
    /// <summary>Keys from the SSH config or the default key files in %USERPROFILE%\.ssh.</summary>
    DefaultKeys,
    /// <summary>One private key file chosen by the user.</summary>
    KeyFile,
    /// <summary>A password, asked for on every connect and never stored.</summary>
    Password,
}

/// <summary>A saved server.</summary>
public sealed class ServerProfile
{
    /// <summary>Stable id used in saved settings.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Name shown in the app, such as "Tower".</summary>
    public string Name { get; set; } = "";

    /// <summary>Address or SSH config alias.</summary>
    public string Host { get; set; } = "";

    /// <summary>Account to sign in as; empty means the SSH config's User or the Windows user name.</summary>
    public string User { get; set; } = "";

    /// <summary>SSH port; 0 means the SSH config's Port or 22.</summary>
    public int Port { get; set; }

    /// <summary>Sign-in method.</summary>
    public AuthKind Auth { get; set; } = AuthKind.DefaultKeys;

    /// <summary>Private key path when <see cref="Auth"/> is <see cref="AuthKind.KeyFile"/>.</summary>
    public string? KeyFile { get; set; }

    /// <summary>Folder to open after connecting; null means the server's home folder.</summary>
    public string? StartPath { get; set; }

    /// <summary>Folders pinned to the sidebar for this server.</summary>
    public List<string> Pins { get; set; } = [];

    /// <summary>When Portal last connected to this server.</summary>
    public DateTime? LastConnectedUtc { get; set; }

    /// <summary>"user@host" or "host:port" style label.</summary>
    public string Address
    {
        get
        {
            var user = User.Length > 0 ? User + "@" : "";
            var port = Port is 0 or 22 ? "" : ":" + Port;
            return user + Host + port;
        }
    }
}
