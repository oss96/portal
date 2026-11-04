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
        await fs.SetPermissionsAsync([file], PermissionDelta.Between(0x1A4, 0x1E4), null, null, recursive: false, default);
        Assert.Equal(0x1E4, (await fs.StatAsync(file, default))!.Mode);

        await File.WriteAllTextAsync(local, "second version, longer");
        await RunAsync(engine, pc, [(await pc.StatAsync(local, default))!], session.TransferFiles, work, ConflictChoice.Replace);
        Assert.Equal("second version, longer", (await server.Exec($"cat '{work.Replace("'", "'\\''")}/hello world.txt'")).Trim());
        Assert.Equal(0x1E4, (await fs.StatAsync(file, default))!.Mode);

        var download = Directory.CreateDirectory(Path.Combine(_local, "down")).FullName;
        await RunAsync(engine, session.TransferFiles, [(await fs.StatAsync(RemotePath.Combine(work, "site"), default))! with { Name = "site", FullPath = RemotePath.Combine(work, "site") }], pc, download);
        Assert.Equal("body{}", await File.ReadAllTextAsync(Path.Combine(download, "site", "css", "app.css")));

        await fs.RenameAsync(file, RemotePath.Combine(work, "renamed.txt"), overwrite: false, default);
        var renamed = (await fs.StatAsync(RemotePath.Combine(work, "renamed.txt"), default))!;
        var never = new Func<ConflictQuestion, Task<ConflictAnswer>>(_ => throw new InvalidOperationException("no conflict expected"));
        await ServerCopy.RunAsync(fs, [renamed], RemotePath.Combine(work, "site"), move: false, ConflictPolicy.Ask, never, default);
        await ServerCopy.RunAsync(fs, [renamed], RemotePath.Combine(RemotePath.Combine(work, "site"), "css"), move: true, ConflictPolicy.Ask, never, default);
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

    [SshFact]
    public async Task An_sftp_only_account_connects_transfers_and_manages_files_without_a_shell()
    {
        var profile = new ServerProfile { Name = "SFTP only", Host = "127.0.0.1", Port = server.SftpOnlyPort, User = server.User };
        await using var session = await SshConnector.ConnectAsync(profile, new Prompts(), new Progress<ConnectStage>(), default);
        var fs = session.Files;
        Assert.False(fs.CanRunCommands);
        var work = RemotePath.Combine(fs.HomeDirectory, "sftp-only");
        if (await fs.StatAsync(work, default) is not null) await fs.DeleteAsync([work], default);
        await fs.CreateDirectoryAsync(work, default);
        await fs.CreateDirectoryAsync(RemotePath.Combine(work, "nested"), default);

        var local = Path.Combine(_local, "plain.txt");
        await File.WriteAllTextAsync(local, "one");
        var engine = new TransferEngine(() => 2);
        var pc = new LocalFileSystem();
        await RunAsync(engine, pc, [(await pc.StatAsync(local, default))!], session.TransferFiles, work);
        await File.WriteAllTextAsync(local, "two, replaced");
        await RunAsync(engine, pc, [(await pc.StatAsync(local, default))!], session.TransferFiles, work, ConflictChoice.Replace);

        var file = RemotePath.Combine(work, "plain.txt");
        Assert.Equal(13, (await fs.StatAsync(file, default))!.Size);
        Assert.DoesNotContain(await fs.ListAsync(work, default), e => e.Name.EndsWith(".portal-part", StringComparison.Ordinal));

        await fs.SetPermissionsAsync([file], PermissionDelta.Between(0x1A4, 0x1E4), null, null, recursive: false, default);
        Assert.Equal(0x1E4, (await fs.StatAsync(file, default))!.Mode);
        Assert.Equal(13, await fs.DirectorySizeAsync(work, default));
        Assert.Empty(await fs.SearchAsync(work, "plain", 10, default));
        await Assert.ThrowsAsync<ShellUnavailableException>(() => fs.SetPermissionsAsync([file], null, "root", null, false, default));

        await fs.DeleteAsync([work], default);
        Assert.Null(await fs.StatAsync(work, default));
    }

    [SshFact]
    public async Task Symlinks_are_never_followed_when_deleting_renaming_or_moving()
    {
        var sftpOnly = new ServerProfile { Name = "SFTP only", Host = "127.0.0.1", Port = server.SftpOnlyPort, User = server.User };
        await using (var session = await SshConnector.ConnectAsync(sftpOnly, new Prompts(), new Progress<ConnectStage>(), default))
        {
            var fs = session.Files;
            var home = fs.HomeDirectory;
            await server.ExecSftpOnly($"cd {home} && rm -rf links && mkdir -p links/target links/box && echo keep > links/real.txt && echo inside > links/target/f.txt && ln -s real.txt links/filelink && ln -s target links/dirlink && ln -s ../real.txt links/box/inner");
            var links = RemotePath.Combine(home, "links");
            await Assert.ThrowsAsync<SymlinkRefusedException>(() => fs.DeleteAsync([RemotePath.Combine(links, "filelink")], default));
            await Assert.ThrowsAsync<SymlinkRefusedException>(() => fs.DeleteAsync([RemotePath.Combine(links, "dirlink")], default));
            await Assert.ThrowsAsync<SymlinkRefusedException>(() => fs.DeleteAsync([RemotePath.Combine(links, "box")], default));
            await Assert.ThrowsAsync<SymlinkRefusedException>(() => fs.RenameAsync(RemotePath.Combine(links, "filelink"), RemotePath.Combine(links, "renamed"), overwrite: false, default));
            Assert.Equal("keep", (await server.ExecSftpOnly($"cat {home}/links/real.txt")).Trim());
            Assert.Equal("inside", (await server.ExecSftpOnly($"cat {home}/links/target/f.txt")).Trim());
            Assert.NotNull(await fs.StatAsync(RemotePath.Combine(links, "box"), default));
        }

        await using (var session = await SshConnector.ConnectAsync(Profile(), new Prompts(), new Progress<ConnectStage>(), default))
        {
            var fs = session.Files;
            var home = fs.HomeDirectory;
            var links = RemotePath.Combine(home, "links");
            await server.Exec($"cd {home} && rm -rf links && mkdir -p links/target links/dest && echo keep > links/real.txt && ln -s real.txt links/filelink && ln -s target links/dirlink");
            await fs.RenameAsync(RemotePath.Combine(links, "filelink"), RemotePath.Combine(links, "renamed"), overwrite: false, default);
            Assert.Equal("real.txt", (await server.Exec($"readlink {home}/links/renamed")).Trim());
            Assert.Equal("keep", (await server.Exec($"cat {home}/links/real.txt")).Trim());
            var dirlink = (await fs.ListAsync(links, default)).Single(e => e.Name == "dirlink");
            await ServerCopy.RunAsync(fs, [dirlink], RemotePath.Combine(links, "dest"), move: true, ConflictPolicy.Ask, _ => throw new InvalidOperationException(), default);
            Assert.Equal("target", (await server.Exec($"readlink {home}/links/dest/dirlink")).Trim());
            Assert.Equal("", (await server.Exec($"test -d {home}/links/target && echo -n")).Trim());
            await fs.DeleteAsync([RemotePath.Combine(links, "renamed")], default);
            Assert.Equal("keep", (await server.Exec($"cat {home}/links/real.txt")).Trim());
            await fs.DeleteAsync([links], default);
        }
    }

    [SshFact]
    public async Task Replacing_onto_a_symlink_never_writes_through_it_and_renames_never_overwrite()
    {
        await using (var session = await SshConnector.ConnectAsync(Profile(), new Prompts(), new Progress<ConnectStage>(), default))
        {
            var fs = session.Files;
            var home = fs.HomeDirectory;
            await server.Exec($"cd {home} && rm -rf r && mkdir -p r/outside r/folder && echo shared > r/outside/shared.txt && ln -s ../outside/shared.txt r/folder/dest.txt && echo mine > r/folder/a.txt && echo theirs > r/folder/b.txt");
            var folder = RemotePath.Combine(home, "r/folder");
            var local = Path.Combine(_local, "dest.txt");
            await File.WriteAllTextAsync(local, "uploaded");
            var engine = new TransferEngine(() => 1);
            var done = new TaskCompletionSource<IReadOnlyList<TransferItem>>();
            engine.BatchFinished += (_, items) => done.TrySetResult(items);
            await engine.EnqueueAsync(new LocalFileSystem(), [(await new LocalFileSystem().StatAsync(local, default))!], session.TransferFiles, folder,
                ConflictPolicy.Replace, _ => throw new InvalidOperationException(), default);
            var refused = Assert.Single(await done.Task.WaitAsync(TimeSpan.FromSeconds(60)));
            Assert.Equal(TransferStatus.Failed, refused.Status);
            Assert.Contains("symbolic link", refused.Error);
            Assert.Equal("shared", (await server.Exec($"cat {home}/r/outside/shared.txt")).Trim());
            Assert.Equal("../outside/shared.txt", (await server.Exec($"readlink {home}/r/folder/dest.txt")).Trim());
            Assert.Equal("", (await server.Exec($"ls -A {home}/r/folder | grep portal-part || true")).Trim());

            await Assert.ThrowsAsync<RemoteCommandException>(() => fs.RenameAsync(RemotePath.Combine(folder, "a.txt"), RemotePath.Combine(folder, "b.txt"), overwrite: false, default));
            Assert.Equal("theirs", (await server.Exec($"cat {home}/r/folder/b.txt")).Trim());
            await fs.DeleteAsync([RemotePath.Combine(home, "r")], default);
        }

        var sftpOnly = new ServerProfile { Name = "SFTP only", Host = "127.0.0.1", Port = server.SftpOnlyPort, User = server.User };
        await using (var session = await SshConnector.ConnectAsync(sftpOnly, new Prompts(), new Progress<ConnectStage>(), default))
        {
            var home = session.Files.HomeDirectory;
            await server.ExecSftpOnly($"cd {home} && rm -rf r && mkdir -p r/outside r/folder && echo shared > r/outside/shared.txt && ln -s ../outside/shared.txt r/folder/dest.txt");
            var local = Path.Combine(_local, "dest.txt");
            await File.WriteAllTextAsync(local, "uploaded");
            var engine = new TransferEngine(() => 1);
            var finished = new TaskCompletionSource<IReadOnlyList<TransferItem>>();
            engine.BatchFinished += (_, items) => finished.TrySetResult(items);
            await engine.EnqueueAsync(new LocalFileSystem(), [(await new LocalFileSystem().StatAsync(local, default))!], session.TransferFiles, RemotePath.Combine(home, "r/folder"),
                ConflictPolicy.Replace, _ => throw new InvalidOperationException(), default);
            var item = Assert.Single(await finished.Task.WaitAsync(TimeSpan.FromSeconds(60)));
            Assert.Equal(TransferStatus.Failed, item.Status);
            Assert.Contains("symbolic link", item.Error);
            Assert.Equal("shared", (await server.ExecSftpOnly($"cat {home}/r/outside/shared.txt")).Trim());
            await server.ExecSftpOnly($"rm -rf {home}/r");
        }
    }

    [SshFact]
    public async Task Copies_moves_and_merges_never_write_through_links_in_the_destination()
    {
        await using var session = await SshConnector.ConnectAsync(Profile(), new Prompts(), new Progress<ConnectStage>(), default);
        var fs = session.Files;
        var h = fs.HomeDirectory;
        await server.Exec($"cd {h} && rm -rf m && mkdir -p m/out m/src/sub m/dest/sub m/up && echo o > m/out/o.txt && echo new > m/src/f.txt && echo s > m/src/sub/g.txt"
            + " && ln -s ../../out/o.txt m/dest/f.txt && ln -s ../out/o.txt m/dest/single.txt && echo x > m/single.txt && mkdir m/dest/folder");
        var m = RemotePath.Combine(h, "m");
        string Read(string rel) => server.Exec($"cat {h}/m/{rel}").GetAwaiter().GetResult().Trim();

        await Assert.ThrowsAsync<RemoteCommandException>(() => fs.CopyOnServerAsync(m + "/single.txt", m + "/dest/single.txt", merge: false, default));
        await Assert.ThrowsAsync<RemoteCommandException>(() => fs.CopyOnServerAsync(m + "/src", m + "/dest", merge: true, default));
        await Assert.ThrowsAsync<RemoteCommandException>(() => fs.MoveOnServerAsync(m + "/src", m + "/dest", merge: true, default));
        await Assert.ThrowsAsync<RemoteCommandException>(() => fs.MoveOnServerAsync(m + "/single.txt", m + "/dest/folder", merge: false, default));
        Assert.Equal("o", Read("out/o.txt"));
        Assert.Equal("new", Read("src/f.txt"));
        Assert.Equal("x", Read("single.txt"));

        await server.Exec($"cd {h}/m && mkdir -p up/top/deep && ln -s ../../out up/top/sub && ln -s ../../out/o.txt up/top/link.txt && ln -s ../../../out/o.txt up/top/deep/.p.txt.portal-part");
        var tree = Directory.CreateDirectory(Path.Combine(_local, "top")).FullName;
        Directory.CreateDirectory(Path.Combine(tree, "sub"));
        Directory.CreateDirectory(Path.Combine(tree, "deep"));
        await File.WriteAllTextAsync(Path.Combine(tree, "sub", "g.txt"), "uploaded");
        await File.WriteAllTextAsync(Path.Combine(tree, "link.txt"), "uploaded");
        await File.WriteAllTextAsync(Path.Combine(tree, "deep", "p.txt"), "uploaded");
        await File.WriteAllTextAsync(Path.Combine(tree, "plain.txt"), "uploaded");
        var engine = new TransferEngine(() => 2);
        var done = new TaskCompletionSource<IReadOnlyList<TransferItem>>();
        engine.BatchFinished += (_, items) => done.TrySetResult(items);
        await engine.EnqueueAsync(new LocalFileSystem(), [(await new LocalFileSystem().StatAsync(tree, default))!], session.TransferFiles, m + "/up",
            ConflictPolicy.Replace, _ => throw new InvalidOperationException(), default);
        var item = Assert.Single(await done.Task.WaitAsync(TimeSpan.FromSeconds(60)));
        Assert.Equal(TransferStatus.Done, item.Status);
        Assert.Equal(["link.txt", "p.txt", "sub"], item.SkippedLinks.Order().ToArray());
        Assert.Equal("uploaded", Read("up/top/plain.txt"));
        Assert.Equal("o", Read("out/o.txt"));
        Assert.Equal("", (await server.Exec($"ls {h}/m/out | grep -v '^o.txt$' || true")).Trim());
        await fs.DeleteAsync([m], default);
    }

    [SshFact]
    public async Task A_known_server_that_hangs_up_is_not_reported_as_a_changed_identity()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var serve = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            await stream.WriteAsync("SSH-2.0-OpenSSH_9.9\r\n"u8.ToArray());
            await Task.Delay(300);
        });
        var path = Path.Combine(server.SshDirectory, "known_hosts");
        var ed = Convert.ToBase64String(new byte[] { 0, 0, 0, 11 }.Concat("ssh-ed25519"u8.ToArray()).Concat(new byte[] { 0, 0, 0, 32 }).Concat(new byte[32]).ToArray());
        await File.WriteAllTextAsync(path, $"[127.0.0.1]:{port} ssh-ed25519 {ed}\n");
        try
        {
            var profile = new ServerProfile { Name = "Flaky", Host = "127.0.0.1", Port = port, User = server.User };
            var e = await Assert.ThrowsAnyAsync<Exception>(() => SshConnector.ConnectAsync(profile, new Prompts(), new Progress<ConnectStage>(), default));
            Assert.IsNotType<HostKeyTypeMismatchException>(e);
            Assert.DoesNotContain("identity", FriendlyError.ForConnect(e, "127.0.0.1", port, server.User).Title);
        }
        finally
        {
            File.Delete(path);
            await serve;
        }
    }

    [SshFact]
    public async Task A_server_without_the_recorded_key_type_reads_as_a_changed_identity()
    {
        var path = Path.Combine(server.SshDirectory, "known_hosts");
        var dss = Convert.ToBase64String(new byte[] { 0, 0, 0, 7 }.Concat("ssh-dss"u8.ToArray()).Concat(new byte[16]).ToArray());
        await File.WriteAllTextAsync(path, $"[127.0.0.1]:{server.Port} ssh-dss {dss}\n");
        try
        {
            var prompts = new Prompts();
            var e = await Assert.ThrowsAsync<HostKeyTypeMismatchException>(() => SshConnector.ConnectAsync(Profile(), prompts, new Progress<ConnectStage>(), default));
            Assert.Equal(0, prompts.TrustQuestions);
            Assert.Contains("identity changed", FriendlyError.ForConnect(e, "127.0.0.1", server.Port, server.User).Title);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [SshFact]
    public async Task A_key_of_a_type_not_on_record_is_treated_as_changed()
    {
        var path = Path.Combine(server.SshDirectory, "known_hosts");
        File.Delete(path);
        await using (await SshConnector.ConnectAsync(Profile(), new Prompts(), new Progress<ConnectStage>(), default)) { }
        var line = (await File.ReadAllLinesAsync(path)).Single();
        var fields = line.Split(' ');
        var rsa = Convert.ToBase64String(new byte[] { 0, 0, 0, 7 }.Concat("ssh-rsa"u8.ToArray()).Concat(new byte[] { 0, 0, 0, 1, 3, 0, 0, 0, 1, 1 }).ToArray());
        await File.WriteAllTextAsync(path, $"{fields[0]} ssh-rsa {rsa}\n");
        try
        {
            var prompts = new Prompts();
            await Assert.ThrowsAnyAsync<Exception>(() => SshConnector.ConnectAsync(Profile(), prompts, new Progress<ConnectStage>(), default));
            Assert.Equal(0, prompts.TrustQuestions);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [SshFact]
    public async Task Permission_changes_touch_only_the_changed_bits_and_server_copies_ask_about_conflicts()
    {
        await using var session = await SshConnector.ConnectAsync(Profile(), new Prompts(), new Progress<ConnectStage>(), default);
        var fs = session.Files;
        var work = RemotePath.Combine(fs.HomeDirectory, "perm-copy");
        await fs.DeleteAsync([work], default);
        var q = work.Replace("'", "'\\''");
        await server.Exec($"mkdir -p '{q}/tree/sub' '{q}/dest' && echo a > '{q}/tree/script.sh' && echo b > '{q}/tree/sub/notes.txt' && chmod 700 '{q}/tree/script.sh' && chmod 640 '{q}/tree/sub/notes.txt' && chmod 755 '{q}/tree' '{q}/tree/sub' && echo old > '{q}/dest/script.sh' && echo d > '{q}/doc.txt'");

        await fs.SetPermissionsAsync([RemotePath.Combine(work, "tree")], PermissionDelta.Between(0x1ED, 0x1FD), null, null, recursive: true, default);
        Assert.Equal(0x1FD, (await fs.StatAsync(RemotePath.Combine(work, "tree"), default))!.Mode);
        Assert.Equal(0x1D0, (await fs.StatAsync(work + "/tree/script.sh", default))!.Mode);
        Assert.Equal(0x1B0, (await fs.StatAsync(work + "/tree/sub/notes.txt", default))!.Mode);

        var script = (await fs.StatAsync(work + "/tree/script.sh", default))!;
        var asked = new List<ConflictQuestion>();
        var result = await ServerCopy.RunAsync(fs, [script], RemotePath.Combine(work, "dest"), move: false, ConflictPolicy.Ask, question =>
        {
            asked.Add(question);
            return Task.FromResult(new ConflictAnswer(ConflictChoice.KeepBoth, false));
        }, default);
        Assert.Single(asked);
        Assert.Equal(["script (1).sh"], result.Placed);
        Assert.Equal("old", (await server.Exec($"cat '{q}/dest/script.sh'")).Trim());
        Assert.Equal("a", (await server.Exec($"cat '{q}/dest/script (1).sh'")).Trim());

        var doc = (await fs.StatAsync(work + "/doc.txt", default))!;
        var copies = await ServerCopy.RunAsync(fs, [doc, doc], work, move: false, ConflictPolicy.Ask, _ => throw new InvalidOperationException("same-folder copies never ask"), default);
        Assert.Equal(["doc - Copy.txt", "doc - Copy (2).txt"], copies.Placed);

        await ServerCopy.RunAsync(fs, [script], RemotePath.Combine(work, "dest"), move: true, ConflictPolicy.Replace, _ => throw new InvalidOperationException("policy answers"), default);
        Assert.Equal("a", (await server.Exec($"cat '{q}/dest/script.sh'")).Trim());
        Assert.Null(await fs.StatAsync(work + "/tree/script.sh", default));

        await fs.DeleteAsync([work], default);
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
