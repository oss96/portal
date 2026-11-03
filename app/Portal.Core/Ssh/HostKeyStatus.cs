namespace Portal.Core.Ssh;

/// <summary>How a server's key compares with what <c>known_hosts</c> records for that host.</summary>
public enum HostKeyStatus
{
    /// <summary>The key is recorded for this host.</summary>
    Trusted,

    /// <summary>The host has no recorded key of this type.</summary>
    Unknown,

    /// <summary>The host has a different key of this type, or this key is revoked.</summary>
    Changed,
}
