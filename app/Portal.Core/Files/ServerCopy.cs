using Portal.Core.Settings;
using Portal.Core.Transfers;

namespace Portal.Core.Files;

/// <summary>What a copy or move on the server did.</summary>
public sealed record ServerCopyResult(IReadOnlyList<string> Placed, int Skipped, bool Stopped);

/// <summary>Copies and moves entries within one server, asking about name conflicts the same way transfers do.</summary>
public static class ServerCopy
{
    /// <summary>Copies or moves <paramref name="sources"/> into <paramref name="targetDirectory"/>. Copying into the same folder makes "name - Copy" duplicates.</summary>
    public static async Task<ServerCopyResult> RunAsync(IServerFileSystem fs, IReadOnlyList<FileEntry> sources, string targetDirectory, bool move,
        ConflictPolicy policy, Func<ConflictQuestion, Task<ConflictAnswer>> ask, CancellationToken ct)
    {
        var target = RemotePath.Normalize(targetDirectory);
        var existing = (await fs.ListAsync(target, ct).ConfigureAwait(false)).ToDictionary(e => e.Name, StringComparer.Ordinal);
        var taken = new HashSet<string>(existing.Keys, StringComparer.Ordinal);
        var conflicts = sources.Count(s => existing.ContainsKey(s.Name) && RemotePath.Parent(s.FullPath) != target);
        ConflictChoice? standing = policy switch
        {
            ConflictPolicy.Replace => ConflictChoice.Replace,
            ConflictPolicy.Skip => ConflictChoice.Skip,
            ConflictPolicy.KeepBoth => ConflictChoice.KeepBoth,
            _ => null,
        };
        var placed = new List<string>();
        var skipped = 0;
        foreach (var source in sources)
        {
            ct.ThrowIfCancellationRequested();
            var sameFolder = RemotePath.Parent(source.FullPath) == target;
            if (sameFolder)
            {
                if (move) { skipped++; continue; }
                var copyName = CopyName(taken, source.Name);
                await fs.CopyOnServerAsync(source.FullPath, RemotePath.Combine(target, copyName), merge: false, ct).ConfigureAwait(false);
                taken.Add(copyName);
                placed.Add(copyName);
                continue;
            }
            if (source.IsDirectory && RemotePath.IsWithin(target, source.FullPath))
                throw new InvalidOperationException($"“{source.Name}” can’t go inside itself.");
            if (!existing.TryGetValue(source.Name, out var current))
            {
                await Place(fs, source, RemotePath.Combine(target, source.Name), move, merge: false, ct).ConfigureAwait(false);
                taken.Add(source.Name);
                placed.Add(source.Name);
                continue;
            }
            conflicts--;
            var keepBoth = TransferEngine.UniqueName(taken, source.Name);
            var question = new ConflictQuestion(source, current, TransferDirection.Upload, fs.DisplayName, keepBoth, conflicts) { IncomingLabel = move ? "Being moved" : "Being copied" };
            var choice = standing;
            if (choice is null || (choice == ConflictChoice.Replace && !question.CanReplace))
            {
                var answer = await ask(question).ConfigureAwait(false);
                choice = answer.Choice;
                if (answer.ApplyToRest && question.CanReplace) standing = answer.Choice;
            }
            if (choice == ConflictChoice.Replace && !question.CanReplace) choice = ConflictChoice.KeepBoth;
            switch (choice)
            {
                case ConflictChoice.Cancel:
                    return new ServerCopyResult(placed, skipped, Stopped: true);
                case ConflictChoice.Skip:
                    skipped++;
                    break;
                case ConflictChoice.KeepBoth:
                    await Place(fs, source, RemotePath.Combine(target, keepBoth), move, merge: false, ct).ConfigureAwait(false);
                    taken.Add(keepBoth);
                    placed.Add(keepBoth);
                    break;
                default:
                    await Place(fs, source, RemotePath.Combine(target, source.Name), move, merge: source.IsDirectory, ct).ConfigureAwait(false);
                    placed.Add(source.Name);
                    break;
            }
        }
        return new ServerCopyResult(placed, skipped, Stopped: false);
    }

    /// <summary>Explorer-style duplicate name: "report - Copy.pdf", then "report - Copy (2).pdf".</summary>
    public static string CopyName(ISet<string> taken, string name)
    {
        var dot = name.LastIndexOf('.');
        var stem = dot > 0 ? name[..dot] : name;
        var ext = dot > 0 ? name[dot..] : "";
        var candidate = $"{stem} - Copy{ext}";
        for (var i = 2; taken.Contains(candidate); i++) candidate = $"{stem} - Copy ({i}){ext}";
        return candidate;
    }

    private static Task Place(IServerFileSystem fs, FileEntry source, string destination, bool move, bool merge, CancellationToken ct) =>
        move ? fs.MoveOnServerAsync(source.FullPath, destination, merge, ct) : fs.CopyOnServerAsync(source.FullPath, destination, merge, ct);
}
