using System.Diagnostics;
using System.Net.Sockets;
using Portal.Core;

namespace Portal.Tests.Integration;

/// <summary>A fact that needs Docker; it runs only when PORTAL_SSH_TESTS=1.</summary>
public sealed class SshFactAttribute : FactAttribute
{
    /// <summary>Skips unless the environment opts in.</summary>
    public SshFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PORTAL_SSH_TESTS") != "1")
            Skip = "starts an OpenSSH container in Docker; set PORTAL_SSH_TESTS=1";
    }
}

/// <summary>Starts a throwaway OpenSSH server in Docker with a freshly generated key, and removes it afterwards.</summary>
public sealed class SshServerFixture : IAsyncLifetime
{
    private const string Image = "lscr.io/linuxserver/openssh-server:latest";
    private string? _container;

    /// <summary>Folder used as %USERPROFILE%\.ssh for the tests.</summary>
    public string SshDirectory { get; } = Directory.CreateTempSubdirectory("portal-ssh-").FullName;

    /// <summary>Host port mapped to the container's sshd.</summary>
    public int Port { get; private set; }

    /// <summary>Account inside the container.</summary>
    public string User => "portal";

    /// <summary>Password of <see cref="User"/>, generated per run.</summary>
    public string Password { get; } = Guid.NewGuid().ToString("N");

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("PORTAL_SSH_TESTS") != "1") return;
        var key = Path.Combine(SshDirectory, "id_ed25519");
        await Run("ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-C", "portal-tests", "-f", key);
        var publicKey = (await File.ReadAllTextAsync(key + ".pub")).Trim();
        _container = (await Run("docker", "run", "-d", "--rm", "-p", "127.0.0.1::2222",
            "-e", $"PUBLIC_KEY={publicKey}", "-e", $"USER_NAME={User}", "-e", "PASSWORD_ACCESS=true", "-e", $"USER_PASSWORD={Password}", Image)).Trim();
        var mapping = (await Run("docker", "port", _container, "2222/tcp")).Trim().Split('\n')[0];
        Port = int.Parse(mapping[(mapping.LastIndexOf(':') + 1)..]);
        await WaitForBannerAsync();
        AppPaths.SshDirectory = SshDirectory;
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_container is not null) await Run("docker", "rm", "-f", _container);
        Directory.Delete(SshDirectory, recursive: true);
    }

    /// <summary>Runs a command inside the container as the test user.</summary>
    public Task<string> Exec(string command) => Run("docker", "exec", "-u", User, _container!, "sh", "-c", command);

    private async Task WaitForBannerAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync("127.0.0.1", Port);
                var buffer = new byte[64];
                tcp.ReceiveTimeout = 2000;
                var n = await tcp.GetStream().ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
                if (n > 0 && System.Text.Encoding.ASCII.GetString(buffer, 0, n).StartsWith("SSH-", StringComparison.Ordinal)) return;
            }
            catch (Exception e) when (e is SocketException or IOException or TimeoutException)
            {
            }
            await Task.Delay(500);
        }
        throw new TimeoutException("The test SSH server did not start.");
    }

    private static async Task<string> Run(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = await p.StandardOutput.ReadToEndAsync();
        var error = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new InvalidOperationException($"{file} {string.Join(' ', args.Take(2))} failed: {error}");
        return output;
    }
}

/// <summary>Serializes tests that change <see cref="AppPaths"/>.</summary>
[CollectionDefinition("ssh", DisableParallelization = true)]
public sealed class SshCollection : ICollectionFixture<SshServerFixture>;
