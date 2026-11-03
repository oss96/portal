using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Portal.Core.Connections;
using Portal.Core.Files;

namespace Portal.Services;

/// <summary>Opens server files in their Windows app and uploads them back each time they are saved.</summary>
public sealed class RemoteEditor(IServerSession session, ToastService toasts) : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Dictionary<string, CancellationTokenSource> _pending = [];
    private readonly Dictionary<string, SemaphoreSlim> _uploading = [];

    /// <summary>Downloads the file to a private temp folder, opens it, and starts watching it.</summary>
    public async Task OpenAsync(FileEntry file)
    {
        var folder = Path.Combine(Path.GetTempPath(), "Portal", "edit", session.Profile.Id, Hash(file.FullPath));
        Directory.CreateDirectory(folder);
        var local = Path.Combine(folder, file.Name);
        await using (var target = LocalFileSystem.OpenWrite(local, append: false))
            await session.TransferFiles.ReadToAsync(file.FullPath, target, 0, new Progress<long>(), CancellationToken.None);
        File.SetLastWriteTimeUtc(local, DateTime.UtcNow);
        Watch(local, file.FullPath);
        Process.Start(new ProcessStartInfo(local) { UseShellExecute = true });
        toasts.Show(ToastKind.Info, $"Opened {file.Name}", $"Save it in the app and Portal uploads the change to {session.Profile.Name}.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
        foreach (var c in _pending.Values) c.Cancel();
    }

    private void Watch(string local, string remote)
    {
        if (_watchers.Any(w => Path.Combine(w.Path, w.Filter) == local)) return;
        var watcher = new FileSystemWatcher(Path.GetDirectoryName(local)!, Path.GetFileName(local)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
        watcher.Changed += (_, _) => Schedule(local, remote);
        watcher.Renamed += (_, e) => { if (e.FullPath == local) Schedule(local, remote); };
        watcher.EnableRaisingEvents = true;
        _watchers.Add(watcher);
    }

    private void Schedule(string local, string remote)
    {
        lock (_pending)
        {
            if (_pending.Remove(local, out var old)) old.Cancel();
            var cts = new CancellationTokenSource();
            _pending[local] = cts;
            _ = UploadSoonAsync(local, remote, cts.Token);
        }
    }

    private async Task UploadSoonAsync(string local, string remote, CancellationToken ct)
    {
        SemaphoreSlim gate;
        lock (_pending)
        {
            if (!_uploading.TryGetValue(local, out gate!)) _uploading[local] = gate = new SemaphoreSlim(1, 1);
        }
        try
        {
            await Task.Delay(800, ct);
            await gate.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        try
        {
            var fs = session.TransferFiles;
            var part = RemotePath.Combine(RemotePath.Parent(remote) ?? "/", $".{RemotePath.NameOf(remote)}.portal-part");
            var mode = (await fs.StatAsync(remote, CancellationToken.None))?.Mode;
            await using (var source = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                await fs.WriteFromAsync(part, source, append: false, new Progress<long>(), CancellationToken.None);
            await fs.RenameAsync(part, remote, overwrite: true, CancellationToken.None);
            if (mode is int m) await fs.SetModeAsync(remote, m, CancellationToken.None);
            toasts.Show(ToastKind.Success, $"Saved {RemotePath.NameOf(remote)} to {session.Profile.Name}", remote);
        }
        catch (Exception e)
        {
            toasts.Show(ToastKind.Error, $"Couldn’t save {RemotePath.NameOf(remote)} to {session.Profile.Name}", FriendlyError.ForOperation(e));
        }
        finally
        {
            gate.Release();
        }
    }

    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..12];
}
