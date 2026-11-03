using Portal.Core.Connections;
using Portal.Core.Files;
using Portal.Core.Settings;
using Portal.Core.Transfers;

namespace Portal.Tests.Integration;

[Collection("ssh")]
public sealed class SshIntegrationTests(SshServerFixture server) : IDisposable
{
    private readonly string _local = Directory.CreateTempSubdirectory("portal-ssh-local-").FullName;

    public void Dispose() => Directory.Delete(_local, recursive: true);

    private ServerProfile Profile(AuthKind auth = AuthKind.DefaultKeys) => new() { Name = "Test", Host = "127.0.0.1", Port = server.Port, User = server.User, Auth = auth };

    [SshFact]
    public async Task First_connect_asks_once_then_remembers_the_host_key()
    {
        File.Delete(Path.Combine(server.SshDirectory, "known_hosts"));
        var prompts = new Prompts();
        await using (var s = await SshConnector.ConnectAsync(Profile(), prompts, new Progress<ConnectStage>(), default)) Assert.True(s.IsConnected);
        await using (var s = await SshConnector.ConnectAsync(Profile(), prompts, new Progress<ConnectStage>(), default)) Assert.True(s.IsConnected);
        Assert.Equal(1, prompts.TrustQuestions);
        Assert.Contains($"[127.0.0.1]:{server.Port}", await File.ReadAllTextAsync(Path.Combine(server.SshDirectory, "known_hosts")));
    }

    [SshFact]
    public async Task Declining_the_host_key_cancels_the_connect()
    {
        File.Delete(Path.Combine(server.SshDirectory, "known_hosts"));
        await Assert.ThrowsAsync<ConnectCancelledException>(() => SshConnector.ConnectAsync(Profile(), new Prompts { Trust = false }, new Progress<ConnectStage>(), default));
    }

    [SshFact]
    public async Task A_changed_host_key_is_refused()
    {
        var path = Path.Combine(server.SshDirectory, "known_hosts");
        await using (await SshConnector.ConnectAsync(Profile(), new Prompts(), new Progress<ConnectStage>(), default)) { }
        var lines = await File.ReadAllLinesAsync(path);
        var fake = Convert.ToBase64String(new byte[] { 0, 0, 0, 11 }.Concat("ssh-ed25519"u8.ToArray()).Concat(new byte[] { 0, 0, 0, 32 }).Concat(new byte[32]).ToArray());
        await File.WriteAllLinesAsync(path, lines.Select(l => string.Join(' ', l.Split(' ')[0], l.Split(' ')[1], fake)));
        try
        {
            var e = await Assert.ThrowsAsync<HostKeyChangedException>(() => SshConnector.ConnectAsync(Profile(), new Prompts(), new Progress<ConnectStage>(), default));
            Assert.Contains("changed", FriendlyError.ForConnect(e, "127.0.0.1", server.Port, server.User).Title);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [SshFact]
    public async Task Password_sign_in_works_and_a_wrong_password_reads_as_rejected()
    {
        await using (var s = await SshConnector.ConnectAsync(Profile(AuthKind.Password), new Prompts { Password = server.Password }, new Progress<ConnectStage>(), default)) Assert.True(s.IsConnected);
        var e = await Assert.ThrowsAnyAsync<Exception>(() => SshConnector.ConnectAsync(Profile(AuthKind.Password), new Prompts { Password = "wrong" }, new Progress<ConnectStage>(), default));
        Assert.Contains("didn’t accept", FriendlyError.ForConnect(e, "127.0.0.1", server.Port, server.User).Title);
    }

    [SshFact]
    public async Task Browse_manage_and_transfer_against_a_real_sftp_server()
    {
        await using var session = await SshConnector.ConnectAsync(Profile(), new Prompts(), new Progress<ConnectStage>(), default);
        var fs = session.Files;
        var home = fs.HomeDirectory;
        var work = RemotePath.Combine(home, "portal test's dir");
        await fs.DeleteAsync([work], default);
        await fs.CreateDirectoryAsync(work, default);

        var local = Path.Combine(_local, "hello world.txt");
        await File.WriteAllTextAsync(local, "hi from windows");
        File.SetLastWriteTimeUtc(local, new DateTime(2024, 2, 3, 4, 5, 6, DateTimeKind.Utc));
        Directory.CreateDirectory(Path.Combine(_local, "site", "css"));
        await File.WriteAllTextAsync(Path.Combine(_local, "site", "css", "app.css"), "body{}");

        var engine = new TransferEngine(() => 3);
        var pc = new LocalFileSystem();
        await RunAsync(engine, pc, [(await pc.StatAsync(local, default))!, (await pc.StatAsync(Path.Combine(_local, "site"), default))!], session.TransferFiles, work);

        var listing = await fs.ListAsync(work, default);
        var uploaded = Assert.Single(listing, e => e.Name == "hello world.txt");
        Assert.Equal(15, uploaded.Size);
        Assert.Equal(new DateTime(2024, 2, 3, 4, 5, 6, DateTimeKind.Utc), uploaded.ModifiedUtc);
        Assert.Equal(server.User, uploaded.Owner);
        Assert.Contains(listing, e => e.Name == "site" && e.IsDirectory);
        Assert.DoesNotContain(listing, e => e.Name.EndsWith(".portal-part", StringComparison.Ordinal));
        Assert.Equal("body{}", (await server.Exec($"cat '{work.Replace("'", "'\\''")}/site/css/app.css'")).Trim());

        var file = RemotePath.Combine(work, "hello world.txt");
        await fs.SetPermissionsAsync([file], 0x1E4, null, null, recursive: false, default);
        Assert.Equal(0x1E4, (await fs.StatAsync(file, default))!.Mode);

        await File.WriteAllTextAsync(local, "second version, longer");
        await RunAsync(engine, pc, [(await pc.StatAsync(local, default))!], session.TransferFiles, work, ConflictChoice.Replace);
        Assert.Equal("second version, longer", (await server.Exec($"cat '{work.Replace("'", "'\\''")}/hello world.txt'")).Trim());
        Assert.Equal(0x1E4, (await fs.StatAsync(file, default))!.Mode);

        var download = Directory.CreateDirectory(Path.Combine(_local, "down")).FullName;
        await RunAsync(engine, session.TransferFiles, [(await fs.StatAsync(RemotePath.Combine(work, "site"), default))! with { Name = "site", FullPath = RemotePath.Combine(work, "site") }], pc, download);
        Assert.Equal("body{}", await File.ReadAllTextAsync(Path.Combine(download, "site", "css", "app.css")));

        await fs.RenameAsync(file, RemotePath.Combine(work, "renamed.txt"), overwrite: false, default);
        await fs.CopyOnServerAsync([RemotePath.Combine(work, "renamed.txt")], RemotePath.Combine(work, "site"), default);
        await fs.MoveOnServerAsync([RemotePath.Combine(work, "renamed.txt")], RemotePath.Combine(RemotePath.Combine(work, "site"), "css"), default);
        var site = await fs.ListAsync(RemotePath.Combine(work, "site"), default);
        Assert.Contains(site, e => e.Name == "renamed.txt");
        Assert.Contains(await fs.ListAsync(RemotePath.Combine(RemotePath.Combine(work, "site"), "css"), default), e => e.Name == "renamed.txt");

        Assert.True(await fs.DirectorySizeAsync(work, default) > 0);
        Assert.Contains(await fs.SearchAsync(work, "RENAMED", 20, default), e => e.Name == "renamed.txt");
        Assert.Contains(await fs.SearchAsync(work, "css", 20, default), e => e.Name == "css" && e.IsDirectory);
        Assert.NotEmpty(await fs.DisksAsync(default));
        Assert.Contains(server.User, fs.Accounts.UserNames);

        await fs.DeleteAsync([work], default);
        Assert.Null(await fs.StatAsync(work, default));
    }

    private static async Task RunAsync(TransferEngine engine, IFileSystem from, IReadOnlyList<FileEntry> sources, IFileSystem to, string folder, ConflictChoice answer = ConflictChoice.Replace)
    {
        var finished = new TaskCompletionSource<IReadOnlyList<TransferItem>>();
        void OnFinished(TransferBatch _, IReadOnlyList<TransferItem> items) => finished.TrySetResult(items);
        engine.BatchFinished += OnFinished;
        try
        {
            var batch = await engine.EnqueueAsync(from, sources, to, folder, ConflictPolicy.Ask, _ => Task.FromResult(new ConflictAnswer(answer, true)), default);
            Assert.NotNull(batch);
            var items = await finished.Task.WaitAsync(TimeSpan.FromSeconds(60));
            Assert.All(items, i => Assert.True(i.Status == TransferStatus.Done, $"{i.Source.Name}: {i.Status} {i.Error}"));
        }
        finally
        {
            engine.BatchFinished -= OnFinished;
        }
    }

    private sealed class Prompts : IConnectPrompts
    {
        public bool Trust { get; init; } = true;
        public string? Password { get; init; }
        public int TrustQuestions { get; private set; }

        public Task<bool> TrustNewHostAsync(HostKeyInfo key)
        {
            TrustQuestions++;
            return Task.FromResult(Trust);
        }

        public Task<string?> AskPassphraseAsync(string keyPath, bool retry) => Task.FromResult<string?>(null);

        public Task<string?> AskPasswordAsync(string user, string host, bool retry) => Task.FromResult(Password);
    }
}
