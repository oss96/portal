using System.Diagnostics;
using Portal.Core.Ssh;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Portal.Core.Connections;

/// <summary>Opens a <see cref="RemoteSession"/> for a saved server.</summary>
public static class SshConnector
{
    private static readonly string[] DefaultKeyNames = ["id_ed25519", "id_ecdsa", "id_rsa"];
    private static readonly TimeSpan PromptTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan NormalTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(30);

    /// <summary>Where a profile actually connects, after SSH config lookup.</summary>
    public sealed record Target(string HostName, int Port, string User, IReadOnlyList<string> IdentityFiles);

    /// <summary>Resolves host, port, user and key files from the profile and %USERPROFILE%\.ssh\config.</summary>
    public static Target Resolve(ServerProfile profile)
    {
        var config = SshConfig.Load(Path.Combine(AppPaths.SshDirectory, "config")).Resolve(profile.Host);
        var port = profile.Port > 0 ? profile.Port : config.Port ?? 22;
        var user = profile.User.Length > 0 ? profile.User : config.User ?? Environment.UserName;
        return new Target(config.HostName, port, user, config.IdentityFiles);
    }

    /// <summary>Connects, asking the user through <paramref name="prompts"/> when a decision or secret is needed.</summary>
    public static async Task<RemoteSession> ConnectAsync(ServerProfile profile, IConnectPrompts prompts, IProgress<ConnectStage> stage, CancellationToken ct)
    {
        stage.Report(ConnectStage.Reaching);
        var target = Resolve(profile);
        var credentials = await Credentials.CreateAsync(profile, target, prompts).ConfigureAwait(false);
        var knownHosts = new KnownHosts(Path.Combine(AppPaths.SshDirectory, "known_hosts"));

        var browse = await ConnectBrowseAsync(credentials, knownHosts, target, prompts, stage, ct).ConfigureAwait(false);
        browse.ConnectionInfo.Timeout = NormalTimeout;
        browse.OperationTimeout = TimeSpan.FromMinutes(2);

        stage.Report(ConnectStage.OpeningFiles);
        var transfer = new SftpClient(credentials.ConnectionInfo(target, NormalTimeout)) { KeepAliveInterval = KeepAlive, OperationTimeout = TimeSpan.FromMinutes(5) };
        var shell = new SshClient(credentials.ConnectionInfo(target, NormalTimeout)) { KeepAliveInterval = KeepAlive };
        var strict = new HostKeyVerifier(knownHosts, target, prompts, allowPrompt: false, stage);
        transfer.HostKeyReceived += strict.OnHostKey;
        shell.HostKeyReceived += strict.OnHostKey;
        try
        {
            await Task.WhenAll(transfer.ConnectAsync(ct), shell.ConnectAsync(ct)).ConfigureAwait(false);
            var namesOutput = await new RemoteShell(shell).RunAsync(AccountNames.ReadCommand, ct).ConfigureAwait(false);
            var latency = await MeasureLatencyAsync(browse, ct).ConfigureAwait(false);
            profile.LastConnectedUtc = DateTime.UtcNow;
            return new RemoteSession(profile, target.HostName, target.Port, target.User, browse, transfer, shell, AccountNames.Parse(namesOutput.Output), latency);
        }
        catch
        {
            foreach (var c in new BaseClient[] { browse, transfer, shell }) c.Dispose();
            throw;
        }
    }

    internal static async Task<TimeSpan> MeasureLatencyAsync(SftpClient client, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        await client.GetAttributesAsync(client.WorkingDirectory, ct).ConfigureAwait(false);
        return sw.Elapsed;
    }

    private static async Task<SftpClient> ConnectBrowseAsync(Credentials credentials, KnownHosts knownHosts, Target target, IConnectPrompts prompts, IProgress<ConnectStage> stage, CancellationToken ct)
    {
        while (true)
        {
            var client = new SftpClient(credentials.ConnectionInfo(target, PromptTimeout)) { KeepAliveInterval = KeepAlive };
            var verifier = new HostKeyVerifier(knownHosts, target, prompts, allowPrompt: true, stage);
            client.HostKeyReceived += verifier.OnHostKey;
            try
            {
                await client.ConnectAsync(ct).ConfigureAwait(false);
                return client;
            }
            catch (SshAuthenticationException) when (credentials.HasLockedKeys)
            {
                client.Dispose();
                await credentials.UnlockKeysAsync(prompts).ConfigureAwait(false);
            }
            catch (Exception) when (verifier.Changed is { } changed)
            {
                client.Dispose();
                throw new HostKeyChangedException(changed);
            }
            catch (Exception) when (verifier.Declined)
            {
                client.Dispose();
                throw new ConnectCancelledException();
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
    }

    /// <summary>Checks host keys against known_hosts, asking once for an unknown server.</summary>
    private sealed class HostKeyVerifier(KnownHosts knownHosts, Target target, IConnectPrompts prompts, bool allowPrompt, IProgress<ConnectStage> stage)
    {
        public HostKeyInfo? Changed { get; private set; }

        public bool Declined { get; private set; }

        public void OnHostKey(object? sender, HostKeyEventArgs e)
        {
            stage.Report(ConnectStage.VerifyingIdentity);
            var info = new HostKeyInfo(target.HostName, target.Port, e.HostKeyName, KnownHosts.Fingerprint(e.HostKey), knownHosts.Path);
            switch (knownHosts.Check(target.HostName, target.Port, e.HostKeyName, e.HostKey))
            {
                case HostKeyStatus.Trusted:
                    e.CanTrust = true;
                    break;
                case HostKeyStatus.Changed:
                    Changed = info;
                    e.CanTrust = false;
                    break;
                default:
                    if (!allowPrompt)
                    {
                        e.CanTrust = false;
                        break;
                    }
                    var trust = prompts.TrustNewHostAsync(info).GetAwaiter().GetResult();
                    if (trust) knownHosts.Add(target.HostName, target.Port, e.HostKeyName, e.HostKey);
                    else Declined = true;
                    e.CanTrust = trust;
                    break;
            }
            if (e.CanTrust) stage.Report(ConnectStage.SigningIn);
        }
    }

    /// <summary>Sign-in material, created once and reused for all three channels.</summary>
    private sealed class Credentials
    {
        private readonly List<IPrivateKeySource> _keys = [];
        private readonly List<string> _lockedKeyPaths = [];
        private string? _password;

        public bool HasLockedKeys => _lockedKeyPaths.Count > 0;

        public static async Task<Credentials> CreateAsync(ServerProfile profile, Target target, IConnectPrompts prompts)
        {
            var c = new Credentials();
            switch (profile.Auth)
            {
                case AuthKind.Password:
                    c._password = await prompts.AskPasswordAsync(target.User, target.HostName, retry: false).ConfigureAwait(false)
                        ?? throw new ConnectCancelledException();
                    break;
                case AuthKind.KeyFile:
                    if (string.IsNullOrWhiteSpace(profile.KeyFile) || !File.Exists(profile.KeyFile))
                        throw new FileNotFoundException($"The key file “{profile.KeyFile}” doesn’t exist.");
                    c.TryLoad(profile.KeyFile);
                    if (c.HasLockedKeys) await c.UnlockKeysAsync(prompts).ConfigureAwait(false);
                    break;
                default:
                    foreach (var path in CandidateKeys(target)) c.TryLoad(path);
                    if (c._keys.Count == 0 && c.HasLockedKeys) await c.UnlockKeysAsync(prompts).ConfigureAwait(false);
                    if (c._keys.Count == 0)
                        throw new FileNotFoundException($"No SSH key found in {AppPaths.SshDirectory}. Choose a key file or sign in with a password.");
                    break;
            }
            return c;
        }

        public async Task UnlockKeysAsync(IConnectPrompts prompts)
        {
            foreach (var path in _lockedKeyPaths.ToArray())
            {
                for (var attempt = 0; ; attempt++)
                {
                    var pass = await prompts.AskPassphraseAsync(path, retry: attempt > 0).ConfigureAwait(false) ?? throw new ConnectCancelledException();
                    try
                    {
                        _keys.Add(new PrivateKeyFile(path, pass));
                        _lockedKeyPaths.Remove(path);
                        break;
                    }
                    catch (SshException) when (attempt < 4)
                    {
                    }
                }
            }
        }

        public ConnectionInfo ConnectionInfo(Target target, TimeSpan timeout)
        {
            var methods = new List<AuthenticationMethod>();
            if (_password is { } pw)
            {
                methods.Add(new PasswordAuthenticationMethod(target.User, pw));
                var ki = new KeyboardInteractiveAuthenticationMethod(target.User);
                ki.AuthenticationPrompt += (_, e) => { foreach (var p in e.Prompts) p.Response = pw; };
                methods.Add(ki);
            }
            if (_keys.Count > 0) methods.Add(new PrivateKeyAuthenticationMethod(target.User, [.. _keys]));
            return new ConnectionInfo(target.HostName, target.Port, target.User, [.. methods]) { Timeout = timeout };
        }

        private static IEnumerable<string> CandidateKeys(Target target)
        {
            var fromConfig = target.IdentityFiles.Where(File.Exists);
            var defaults = DefaultKeyNames.Select(n => Path.Combine(AppPaths.SshDirectory, n)).Where(File.Exists);
            return fromConfig.Concat(defaults).Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private void TryLoad(string path)
        {
            try
            {
                _keys.Add(new PrivateKeyFile(path));
            }
            catch (SshPassPhraseNullOrEmptyException)
            {
                _lockedKeyPaths.Add(path);
            }
            catch (Exception e) when (e is SshException or InvalidOperationException or NotSupportedException or IOException)
            {
            }
        }
    }
}
