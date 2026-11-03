using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Portal.Core.Ssh;

/// <summary>An OpenSSH <c>known_hosts</c> file: checks server keys and records newly trusted ones.</summary>
public sealed class KnownHosts(string path)
{
    private const int DefaultPort = 22;
    private const string HashedPrefix = "|1|";

    /// <summary>The file this instance reads and appends to.</summary>
    public string Path { get; } = path;

    /// <summary>Compares a server key with the entries recorded for <paramref name="host"/> and <paramref name="port"/>.</summary>
    public HostKeyStatus Check(string host, int port, string keyType, byte[] keyBlob)
    {
        if (!File.Exists(Path))
        {
            return HostKeyStatus.Unknown;
        }

        var lookupName = LookupName(host, port);
        var normalizedType = NormalizeKeyType(keyType);
        var sameType = false;
        var trusted = false;

        foreach (var entry in File.ReadLines(Path).Select(ParseLine).OfType<Entry>().Where(e => e.MatchesHost(lookupName)))
        {
            var sameKey = entry.Key.AsSpan().SequenceEqual(keyBlob);
            if (entry.Revoked)
            {
                if (sameKey)
                {
                    return HostKeyStatus.Changed;
                }

                continue;
            }

            if (entry.KeyType != normalizedType)
            {
                continue;
            }

            sameType = true;
            trusted |= sameKey;
        }

        return trusted ? HostKeyStatus.Trusted : sameType ? HostKeyStatus.Changed : HostKeyStatus.Unknown;
    }

    /// <summary>Appends a plain entry for the host, creating the file and its folder when needed.</summary>
    public void Add(string host, int port, string keyType, byte[] keyBlob)
    {
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var line = $"{LookupName(host, port)} {NormalizeKeyType(keyType)} {Convert.ToBase64String(keyBlob)}\n";
        File.AppendAllText(Path, EndsWithoutNewline() ? "\n" + line : line);
    }

    /// <summary>The OpenSSH SHA-256 fingerprint of a public key blob, such as <c>SHA256:uNiVz…</c>.</summary>
    public static string Fingerprint(byte[] keyBlob) =>
        "SHA256:" + Convert.ToBase64String(SHA256.HashData(keyBlob)).TrimEnd('=');

    internal static string LookupName(string host, int port)
    {
        var name = host.ToLowerInvariant();
        return port is DefaultPort or <= 0 ? name : $"[{name}]:{port.ToString(CultureInfo.InvariantCulture)}";
    }

    internal static string NormalizeKeyType(string keyType) =>
        keyType is "rsa-sha2-256" or "rsa-sha2-512" ? "ssh-rsa" : keyType;

    private bool EndsWithoutNewline()
    {
        if (!File.Exists(Path))
        {
            return false;
        }

        using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length == 0)
        {
            return false;
        }

        stream.Seek(-1, SeekOrigin.End);
        return stream.ReadByte() != '\n';
    }

    private static Entry? ParseLine(string line)
    {
        var fields = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length == 0 || fields[0].StartsWith('#'))
        {
            return null;
        }

        var revoked = false;
        var first = 0;
        if (fields[0].StartsWith('@'))
        {
            if (fields[0] != "@revoked")
            {
                return null;
            }

            revoked = true;
            first = 1;
        }

        if (fields.Length < first + 3)
        {
            return null;
        }

        var key = TryDecodeBase64(fields[first + 2]);
        return key is null ? null : new Entry(revoked, fields[first], fields[first + 1], key);
    }

    private static byte[]? TryDecodeBase64(string text)
    {
        var buffer = new byte[text.Length];
        return Convert.TryFromBase64String(text, buffer, out var written) ? buffer[..written] : null;
    }

    private sealed record Entry(bool Revoked, string Hosts, string KeyType, byte[] Key)
    {
        public bool MatchesHost(string lookupName) =>
            Hosts.StartsWith(HashedPrefix, StringComparison.Ordinal)
                ? MatchesHashed(lookupName)
                : HostPatterns.MatchesList(lookupName, Hosts.Split(','));

        private bool MatchesHashed(string lookupName)
        {
            var parts = Hosts[HashedPrefix.Length..].Split('|');
            if (parts.Length != 2)
            {
                return false;
            }

            var salt = TryDecodeBase64(parts[0]);
            var hash = TryDecodeBase64(parts[1]);
            return salt is not null
                && hash is not null
                && HMACSHA1.HashData(salt, Encoding.UTF8.GetBytes(lookupName)).AsSpan().SequenceEqual(hash);
        }
    }
}
