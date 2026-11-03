using Portal.Core.Files;
using Portal.Core.Settings;
using Portal.Core.Transfers;

namespace Portal.Tests;

public sealed class TransferEngineTests : IDisposable
{
    private readonly string _local = Directory.CreateTempSubdirectory("portal-tests-").FullName;
    private readonly LocalFileSystem _pc = new();
    private readonly MemoryFileSystem _server = new();
    private readonly TransferEngine _engine = new(() => 2);

    public TransferEngineTests() => _server.AddDirectory("/data");

    public void Dispose() => Directory.Delete(_local, recursive: true);

    [Fact]
    public async Task Upload_copies_the_file_and_keeps_its_modification_time()
    {
        var file = LocalFile("notes.txt", "hello server");
        var stamp = new DateTime(2025, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, stamp);

        await RunAsync(_pc, [await Stat(file)], _server, "/data");

        Assert.Equal("hello server", _server.Read("/data/notes.txt"));
        Assert.Equal(stamp, _server.ModifiedOf("/data/notes.txt"));
        Assert.DoesNotContain(_server.Paths, p => p.EndsWith(".portal-part", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Upload_of_a_folder_recreates_its_tree()
    {
        Directory.CreateDirectory(Path.Combine(_local, "site", "css"));
        LocalFile(Path.Combine("site", "index.html"), "<h1>hi</h1>");
        LocalFile(Path.Combine("site", "css", "app.css"), "body{}");

        var items = await RunAsync(_pc, [await Stat(Path.Combine(_local, "site"))], _server, "/data");

        Assert.Equal("<h1>hi</h1>", _server.Read("/data/site/index.html"));
        Assert.Equal("body{}", _server.Read("/data/site/css/app.css"));
        Assert.Equal(2, items[0].FilesDone);
    }

    [Fact]
    public async Task Download_writes_to_this_pc()
    {
        _server.AddFile("/data/report.pdf", "pdf bytes");
        var entry = (await _server.StatAsync("/data/report.pdf", default))!;

        await RunAsync(_server, [entry], _pc, _local);

        Assert.Equal("pdf bytes", File.ReadAllText(Path.Combine(_local, "report.pdf")));
    }

    [Fact]
    public async Task Keep_both_saves_under_a_numbered_name()
    {
        _server.AddFile("/data/photo.jpg", "old");
        var file = LocalFile("photo.jpg", "new");

        await RunAsync(_pc, [await Stat(file)], _server, "/data", _ => new ConflictAnswer(ConflictChoice.KeepBoth, false));

        Assert.Equal("old", _server.Read("/data/photo.jpg"));
        Assert.Equal("new", _server.Read("/data/photo (1).jpg"));
    }

    [Fact]
    public async Task Skip_leaves_the_existing_file_and_queues_nothing()
    {
        _server.AddFile("/data/photo.jpg", "old");
        var file = LocalFile("photo.jpg", "new");

        var batch = await _engine.EnqueueAsync(_pc, [await Stat(file)], _server, "/data", ConflictPolicy.Ask, _ => Task.FromResult(new ConflictAnswer(ConflictChoice.Skip, false)), default);

        Assert.Null(batch);
        Assert.Equal("old", _server.Read("/data/photo.jpg"));
    }

    [Fact]
    public async Task Cancel_in_the_conflict_dialog_queues_nothing_at_all()
    {
        _server.AddFile("/data/b.txt", "old");
        var a = LocalFile("a.txt", "a");
        var b = LocalFile("b.txt", "b");

        var batch = await _engine.EnqueueAsync(_pc, [await Stat(a), await Stat(b)], _server, "/data", ConflictPolicy.Ask, _ => Task.FromResult(new ConflictAnswer(ConflictChoice.Cancel, false)), default);

        Assert.Null(batch);
        Assert.Empty(_engine.Snapshot());
    }

    [Fact]
    public async Task Apply_to_rest_answers_later_conflicts_without_asking()
    {
        _server.AddFile("/data/a.txt", "old a");
        _server.AddFile("/data/b.txt", "old b");
        var asked = 0;

        await RunAsync(_pc, [await Stat(LocalFile("a.txt", "new a")), await Stat(LocalFile("b.txt", "new b"))], _server, "/data", _ =>
        {
            asked++;
            return new ConflictAnswer(ConflictChoice.Replace, true);
        });

        Assert.Equal(1, asked);
        Assert.Equal("new a", _server.Read("/data/a.txt"));
        Assert.Equal("new b", _server.Read("/data/b.txt"));
    }

    [Fact]
    public async Task Replace_policy_overwrites_without_asking()
    {
        _server.AddFile("/data/a.txt", "old");
        var batch = await _engine.EnqueueAsync(_pc, [await Stat(LocalFile("a.txt", "new"))], _server, "/data", ConflictPolicy.Replace,
            _ => throw new InvalidOperationException("should not ask"), default);
        await WaitAsync(batch!);
        Assert.Equal("new", _server.Read("/data/a.txt"));
    }

    [Fact]
    public async Task A_failed_item_does_not_stop_the_others_and_retry_resumes_from_the_partial_file()
    {
        var big = LocalFile("big.bin", new string('x', 64));
        var small = LocalFile("small.txt", "ok");
        _server.FailWriteAfter = 20;

        var items = await RunAsync(_pc, [await Stat(big), await Stat(small)], _server, "/data");
        var failed = items.Single(i => i.Source.Name == "big.bin");
        Assert.Equal(TransferStatus.Failed, failed.Status);
        Assert.Equal(TransferStatus.Done, items.Single(i => i.Source.Name == "small.txt").Status);
        Assert.True(_server.Exists("/data/.big.bin.portal-part"));

        var done = new TaskCompletionSource();
        _engine.BatchFinished += (_, _) => done.TrySetResult();
        _engine.Resume(failed);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(TransferStatus.Done, failed.Status);
        Assert.Equal(new string('x', 64), _server.Read("/data/big.bin"));
        Assert.Equal(1, _server.AppendedWrites);
    }

    [Fact]
    public async Task A_new_transfer_ignores_a_leftover_partial_file()
    {
        _server.AddFile("/data/.backup.tar.portal-part", "OLD-VERSION-HALF");
        var file = LocalFile("backup.tar", "brand new contents");

        await RunAsync(_pc, [await Stat(file)], _server, "/data");

        Assert.Equal("brand new contents", _server.Read("/data/backup.tar"));
        Assert.Equal(0, _server.AppendedWrites);
    }

    [Fact]
    public async Task A_download_onto_a_name_that_differs_only_in_case_asks_first()
    {
        File.WriteAllText(Path.Combine(_local, "readme.md"), "mine");
        _server.AddFile("/data/README.md", "theirs");
        var asked = 0;

        await _engine.EnqueueAsync(_server, [(await _server.StatAsync("/data/README.md", default))!], _pc, _local, ConflictPolicy.Ask,
            _ => { asked++; return Task.FromResult(new ConflictAnswer(ConflictChoice.Skip, false)); }, default);

        Assert.Equal(1, asked);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(_local, "readme.md")));
    }

    [Fact]
    public async Task A_file_meeting_a_folder_of_the_same_name_is_asked_about_even_with_replace_as_policy()
    {
        _server.AddDirectory("/data/notes");
        var file = LocalFile("notes", "a file named notes");
        ConflictQuestion? question = null;

        var batch = await _engine.EnqueueAsync(_pc, [await Stat(file)], _server, "/data", ConflictPolicy.Replace,
            q => { question = q; return Task.FromResult(new ConflictAnswer(ConflictChoice.KeepBoth, false)); }, default);
        await WaitAsync(batch!);

        Assert.NotNull(question);
        Assert.False(question!.CanReplace);
        Assert.Equal("a file named notes", _server.Read("/data/notes (1)"));
    }

    [Fact]
    public void Unique_name_counts_up_past_taken_names()
    {
        var taken = new HashSet<string> { "a.txt", "a (1).txt" };
        Assert.Equal("a (2).txt", TransferEngine.UniqueName(taken, "a.txt"));
        Assert.Equal("folder (1)", TransferEngine.UniqueName(new HashSet<string> { "folder" }, "folder"));
        Assert.Equal(".env (1)", TransferEngine.UniqueName(new HashSet<string> { ".env" }, ".env"));
    }

    private string LocalFile(string relative, string text)
    {
        var path = Path.Combine(_local, relative);
        File.WriteAllText(path, text);
        return path;
    }

    private async Task<FileEntry> Stat(string path) => (await _pc.StatAsync(path, default))!;

    private async Task<IReadOnlyList<TransferItem>> RunAsync(IFileSystem from, IReadOnlyList<FileEntry> sources, IFileSystem to, string folder, Func<ConflictQuestion, ConflictAnswer>? ask = null)
    {
        var finished = new TaskCompletionSource<IReadOnlyList<TransferItem>>();
        _engine.BatchFinished += (_, items) => finished.TrySetResult(items);
        var batch = await _engine.EnqueueAsync(from, sources, to, folder, ConflictPolicy.Ask, q => Task.FromResult(ask?.Invoke(q) ?? new ConflictAnswer(ConflictChoice.Replace, false)), default);
        Assert.NotNull(batch);
        return await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private async Task WaitAsync(TransferBatch batch)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (_engine.Snapshot().Where(i => i.Batch == batch).Any(i => i.Status is TransferStatus.Queued or TransferStatus.Running))
        {
            Assert.True(DateTime.UtcNow < deadline, "transfer did not finish");
            await Task.Delay(20);
        }
    }
}
