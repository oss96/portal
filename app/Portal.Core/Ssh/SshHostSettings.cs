namespace Portal.Core.Ssh;

/// <summary>Connection settings an OpenSSH config gives one host alias.</summary>
/// <param name="Alias">The host name as the user typed it.</param>
/// <param name="HostName">The real host to connect to; the alias when the config sets none.</param>
/// <param name="User">The login name, when the config sets one.</param>
/// <param name="Port">The TCP port, when the config sets one.</param>
/// <param name="IdentityFiles">Private key paths in config order, with <c>~</c> and tokens expanded.</param>
public sealed record SshHostSettings(
    string Alias,
    string HostName,
    string? User,
    int? Port,
    IReadOnlyList<string> IdentityFiles);
