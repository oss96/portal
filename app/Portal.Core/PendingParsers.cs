namespace Portal.Core.Shell
{
    public static class PosixShell
    {
        public static string Quote(string arg) => "'" + arg.Replace("'", "'\\''") + "'";
        public static string Join(IEnumerable<string> args) => string.Join(' ', args.Select(Quote));
    }
}

namespace Portal.Core.Ssh
{
    public sealed record SshHostSettings(string Alias, string HostName, string? User, int? Port, IReadOnlyList<string> IdentityFiles);

    public sealed class SshConfig
    {
        public static SshConfig Load(string path) => new();
        public IReadOnlyList<string> HostAliases => [];
        public SshHostSettings Resolve(string host) => new(host, host, null, null, []);
    }

    public enum HostKeyStatus { Trusted, Unknown, Changed }

    public sealed class KnownHosts(string path)
    {
        public string Path => path;
        public HostKeyStatus Check(string host, int port, string keyType, byte[] keyBlob) => HostKeyStatus.Unknown;
        public void Add(string host, int port, string keyType, byte[] keyBlob) { }
        public static string Fingerprint(byte[] keyBlob) => "SHA256:" + Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(keyBlob)).TrimEnd('=');
    }
}
