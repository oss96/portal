using System.Security.Cryptography;
using System.Text;
using Portal.Core.Ssh;

namespace Portal.Tests.Ssh;

public class KnownHostsTests
{
    private static readonly byte[] ServerKey = SyntheticKey("ssh-ed25519", 1);
    private static readonly byte[] OtherKey = SyntheticKey("ssh-ed25519", 2);
    private static readonly byte[] RsaKey = SyntheticKey("ssh-rsa", 3);

    [Fact]
    public void A_missing_file_knows_no_host()
    {
        using var temp = new TempDirectory();

        var status = new KnownHosts(temp.Combine("known_hosts")).Check("web", 22, "ssh-ed25519", ServerKey);

        Assert.Equal(HostKeyStatus.Unknown, status);
    }

    [Fact]
    public void A_recorded_key_is_trusted()
    {
        using var temp = new TempDirectory();
        var knownHosts = Write(temp, $"web ssh-ed25519 {B64(ServerKey)}");

        Assert.Equal(HostKeyStatus.Trusted, knownHosts.Check("web", 22, "ssh-ed25519", ServerKey));
    }

    [Fact]
    public void A_different_key_of_the_same_type_is_changed()
    {
        using var temp = new TempDirectory();
        var knownHosts = Write(temp, $"web ssh-ed25519 {B64(OtherKey)}");

        Assert.Equal(HostKeyStatus.Changed, knownHosts.Check("web", 22, "ssh-ed25519", ServerKey));
    }

    [Fact]
    public void A_key_of_another_type_leaves_the_host_unknown_for_this_type()
    {
        using var temp = new TempDirectory();
        var knownHosts = Write(temp, $"web ssh-rsa {B64(RsaKey)}");

        Assert.Equal(HostKeyStatus.Unknown, knownHosts.Check("web", 22, "ssh-ed25519", ServerKey));
    }

    [Fact]
    public void Any_matching_entry_of_the_type_makes_the_key_trusted()
    {
        using var temp = new TempDirectory();
        var knownHosts = Write(temp, $"web ssh-ed25519 {B64(OtherKey)}", $"web ssh-ed25519 {B64(ServerKey)}");

        Assert.Equal(HostKeyStatus.Trusted, knownHosts.Check("web", 22, "ssh-ed25519", ServerKey));
    }

    [Fact]
    public void Rsa_signature_algorithm_names_match_ssh_rsa_entries()
    {
        using var temp = new TempDirectory();
        var knownHosts = Write(temp, $"web ssh-rsa {B64(RsaKey)}");

        Assert.Equal(HostKeyStatus.Trusted, knownHosts.Check("web", 22, "rsa-sha2-512", RsaKey));
    }

    [Theory]
    [InlineData("web", true)]
    [InlineData("WEB", true)]
    [InlineData("10.0.0.5", true)]
    [InlineData("db", false)]
    public void Comma_separated_patterns_each_name_the_host(string host, bool trusted)
    {
        using var temp = new TempDirectory();
        var knownHosts = Write(temp, $"web,10.0.0.5 ssh-ed25519 {B64(ServerKey)} comment here");

        Assert.Equal(trusted ? HostKeyStatus.Trusted : HostKeyStatus.Unknown, knownHosts.Check(host, 22, "ssh-ed25519", ServerKey));
    }

    [Fact]
    public void Wildcards_and_negation_follow_openssh()
    {
        using var temp = new TempDirectory();
        var knownHosts = Write(temp, $"*.example.org,!evil.example.org ssh-ed25519 {B64(ServerKey)}");

        Assert.Equal(HostKeyStatus.Trusted, knownHosts.Check("web.example.org", 22, "ssh-ed25519", ServerKey));
        Assert.Equal(HostKeyStatus.Unknown, knownHosts.Check("evil.example.org", 22, "ssh-ed25519", ServerKey));
    }

    [Fact]
    public void A_non_default_port_only_matches_bracketed_entries()
    {
        using var temp = new TempDirectory();
        var knownHosts = Write(temp, $"web ssh-ed25519 {B64(OtherKey)}", $"[web]:2222 ssh-ed25519 {B64(ServerKey)}");

        Assert.Equal(HostKeyStatus.Trusted, knownHosts.Check("web", 2222, "ssh-ed25519", ServerKey));
        Assert.Equal(HostKeyStatus.Changed, knownHosts.Check("web", 22, "ssh-ed25519", ServerKey));
        Assert.Equal(HostKeyStatus.Unknown, knownHosts.Check("web", 2200, "ssh-ed25519", ServerKey));
    }

    [Fact]
    public void Hashed_entries_match_their_host_name()
    {
        using var temp = new TempDirectory();
        var knownHosts = Write(temp, $"{Hashed("web.example.org")} ssh-ed25519 {B64(ServerKey)}", $"{Hashed("[db]:2222")} ssh-ed25519 {B64(OtherKey)}");

        Assert.Equal(HostKeyStatus.Trusted, knownHosts.Check("web.example.org", 22, "ssh-ed25519", ServerKey));
        Assert.Equal(HostKeyStatus.Unknown, knownHosts.Check("other.example.org", 22, "ssh-ed25519", ServerKey));
        Assert.Equal(HostKeyStatus.Changed, knownHosts.Check("db", 2222, "ssh-ed25519", ServerKey));
    }

    [Fact]
    public void A_revoked_key_is_reported_as_changed_even_when_also_trusted()
    {
        using var temp = new TempDirectory();
        var knownHosts = Write(temp, $"web ssh-ed25519 {B64(ServerKey)}", $"@revoked * ssh-ed25519 {B64(ServerKey)}");

        Assert.Equal(HostKeyStatus.Changed, knownHosts.Check("web", 22, "ssh-ed25519", ServerKey));
    }

    [Fact]
    public void A_revoked_entry_for_another_key_does_not_affect_this_one()
    {
        using var temp = new TempDirectory();
        var knownHosts = Write(temp, $"@revoked * ssh-ed25519 {B64(OtherKey)}");

        Assert.Equal(HostKeyStatus.Unknown, knownHosts.Check("web", 22, "ssh-ed25519", ServerKey));
    }

    [Fact]
    public void Cert_authority_lines_comments_and_garbage_are_ignored()
    {
        using var temp = new TempDirectory();
        var knownHosts = Write(
            temp,
            "# web ssh-ed25519 AAAA",
            "",
            $"@cert-authority web ssh-ed25519 {B64(OtherKey)}",
            "web ssh-ed25519 not-base64!",
            "web ssh-ed25519");

        Assert.Equal(HostKeyStatus.Unknown, knownHosts.Check("web", 22, "ssh-ed25519", ServerKey));
    }

    [Fact]
    public void Add_creates_the_folder_and_file_and_the_key_is_then_trusted()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine(Path.Combine("nested", ".ssh", "known_hosts"));
        var knownHosts = new KnownHosts(path);

        knownHosts.Add("Web", 22, "ssh-ed25519", ServerKey);

        Assert.Equal($"web ssh-ed25519 {B64(ServerKey)}\n", File.ReadAllText(path));
        Assert.Equal(HostKeyStatus.Trusted, knownHosts.Check("web", 22, "ssh-ed25519", ServerKey));
    }

    [Fact]
    public void Add_writes_bracketed_hosts_for_other_ports()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("known_hosts");

        new KnownHosts(path).Add("web", 2222, "ssh-ed25519", ServerKey);

        Assert.Equal($"[web]:2222 ssh-ed25519 {B64(ServerKey)}\n", File.ReadAllText(path));
    }

    [Fact]
    public void Add_starts_a_new_line_when_the_file_lacks_a_trailing_newline()
    {
        using var temp = new TempDirectory();
        var path = temp.Write("known_hosts", $"db ssh-rsa {B64(RsaKey)}");

        new KnownHosts(path).Add("web", 22, "ssh-ed25519", ServerKey);

        Assert.Equal($"db ssh-rsa {B64(RsaKey)}\nweb ssh-ed25519 {B64(ServerKey)}\n", File.ReadAllText(path));
    }

    [Fact]
    public void Add_appends_directly_after_an_existing_newline()
    {
        using var temp = new TempDirectory();
        var path = temp.Write("known_hosts", $"db ssh-rsa {B64(RsaKey)}\n");

        new KnownHosts(path).Add("web", 22, "ssh-ed25519", ServerKey);

        Assert.Equal($"db ssh-rsa {B64(RsaKey)}\nweb ssh-ed25519 {B64(ServerKey)}\n", File.ReadAllText(path));
    }

    [Fact]
    public void Fingerprint_matches_openssh_format()
    {
        var expected = "SHA256:" + Convert.ToBase64String(SHA256.HashData(ServerKey)).TrimEnd('=');

        var fingerprint = KnownHosts.Fingerprint(ServerKey);

        Assert.Equal(expected, fingerprint);
        Assert.DoesNotContain('=', fingerprint);
        Assert.Equal("SHA256:".Length + 43, fingerprint.Length);
    }

    [Fact]
    public void Fingerprint_of_a_known_vector()
    {
        Assert.Equal("SHA256:47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU", KnownHosts.Fingerprint([]));
    }

    private static KnownHosts Write(TempDirectory temp, params string[] lines) =>
        new(temp.Write("known_hosts", string.Join('\n', lines) + "\n"));

    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes);

    private static string Hashed(string lookupName)
    {
        var salt = RandomNumberGenerator.GetBytes(20);
        var hash = HMACSHA1.HashData(salt, Encoding.UTF8.GetBytes(lookupName));
        return $"|1|{B64(salt)}|{B64(hash)}";
    }

    private static byte[] SyntheticKey(string type, byte fill)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var blob = new byte[4 + typeBytes.Length + 4 + 32];
        blob[3] = (byte)typeBytes.Length;
        typeBytes.CopyTo(blob, 4);
        blob[4 + typeBytes.Length + 3] = 32;
        blob.AsSpan(8 + typeBytes.Length).Fill(fill);
        return blob;
    }
}
