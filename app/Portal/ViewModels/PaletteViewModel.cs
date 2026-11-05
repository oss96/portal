using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Files;

namespace Portal.ViewModels;

/// <summary>One row of the command search.</summary>
public sealed record PaletteItem(string Label, string Glyph, string Hint, Func<Task> Run);

/// <summary>Ctrl+K: run any command, or find files under the current server folder.</summary>
public sealed partial class PaletteViewModel : ObservableObject
{
    private readonly BrowserViewModel _browser;
    private CancellationTokenSource? _search;
    private IReadOnlyList<PaletteItem> _found = [];

    /// <summary>Opens the search over a browser.</summary>
    public PaletteViewModel(BrowserViewModel browser)
    {
        _browser = browser;
        Rebuild();
    }

    /// <summary>Matching commands and files.</summary>
    public ObservableCollection<PaletteItem> Results { get; } = [];

    [ObservableProperty]
    private string _query = "";

    [ObservableProperty]
    private PaletteItem? _selected;

    [ObservableProperty]
    private bool _isSearching;

    /// <summary>Placeholder naming where files are searched.</summary>
    public string Placeholder => $"Type a command, or a file name to find under {_browser.Remote.Path}";

    /// <summary>Moves the highlight up or down.</summary>
    public void Move(int delta)
    {
        if (Results.Count == 0) return;
        var i = Selected is null ? -1 : Results.IndexOf(Selected);
        Selected = Results[(i + delta + Results.Count) % Results.Count];
    }

    [RelayCommand]
    private async Task Run(PaletteItem? item)
    {
        item ??= Selected;
        if (item is null) return;
        Close();
        await item.Run();
    }

    [RelayCommand]
    private void Close()
    {
        _search?.Cancel();
        if (_browser.Main.Palette == this) _browser.Main.Palette = null;
    }

    partial void OnQueryChanged(string value)
    {
        _found = [];
        Rebuild();
        _search?.Cancel();
        if (value.Trim().Length < 2 || value.StartsWith('/')) return;
        if (!_browser.Session.Files.CanRunCommands)
        {
            _found = [new PaletteItem("Searching files isn’t available here", Glyphs.Info, _browser.Session.Files.NoCommandsReason ?? "", () => Task.CompletedTask)];
            Rebuild();
            return;
        }
        var cts = _search = new CancellationTokenSource();
        _ = SearchAsync(value.Trim(), cts.Token);
    }

    private async Task SearchAsync(string text, CancellationToken ct)
    {
        try
        {
            await Task.Delay(250, ct);
            IsSearching = true;
            var root = _browser.Remote.Path;
            var hits = await _browser.Session.Files.SearchAsync(root, text, 60, ct);
            if (ct.IsCancellationRequested) return;
            _found = [.. hits.Where(h => h.FullPath != root).Select(h => new PaletteItem(h.Name, h.IsDirectory ? Glyphs.Folder : Glyphs.Document,
                RemotePath.Parent(h.FullPath) ?? "/", () => Reveal(h)))];
            Rebuild();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            _found = [];
        }
        finally
        {
            if (!ct.IsCancellationRequested) IsSearching = false;
        }
    }

    private Task Reveal(FileEntry hit)
    {
        var remote = _browser.Remote;
        _browser.Activate(remote);
        return hit.IsDirectory ? remote.NavigateAsync(hit.FullPath) : remote.NavigateAsync(RemotePath.Parent(hit.FullPath) ?? "/", [hit.Name]);
    }

    private void Rebuild()
    {
        var q = Query.Trim();
        Results.Clear();
        if (q.StartsWith('/'))
            Results.Add(new PaletteItem($"Go to {q}", Glyphs.ChevronRight, _browser.ServerName, () => _browser.Remote.NavigateAsync(RemotePath.Normalize(q))));
        foreach (var c in Commands().Where(c => q.Length == 0 || c.Label.Contains(q, StringComparison.OrdinalIgnoreCase))) Results.Add(c);
        foreach (var f in _found) Results.Add(f);
        Selected = Results.FirstOrDefault();
    }

    private IEnumerable<PaletteItem> Commands()
    {
        var b = _browser;
        var sel = b.ActivePane.Selected.Count;
        Task Do(Action a) { a(); return Task.CompletedTask; }
        if (sel > 0) yield return new PaletteItem($"{b.TransferText} {Formatting.Count(sel, "selected item")}", b.TransferGlyph, "Ctrl+Enter", () => b.TransferCommand.ExecuteAsync(null));
        yield return new PaletteItem("New folder", Glyphs.NewFolder, "Ctrl+Shift+N", () => b.NewFolderCommand.ExecuteAsync(null));
        if (sel == 1) yield return new PaletteItem("Rename", Glyphs.Rename, "F2", () => Do(() => b.RenameCommand.Execute(null)));
        if (sel > 0 && b.ActivePane.IsRemote) yield return new PaletteItem("Permissions and owner…", Glyphs.Lock, "", () => b.EditPermissionsCommand.ExecuteAsync(null));
        if (sel > 0) yield return new PaletteItem("Delete", Glyphs.Delete, "Del", () => b.DeleteCommand.ExecuteAsync(null));
        yield return new PaletteItem("Refresh", Glyphs.Refresh, "F5", () => b.RefreshCommand.ExecuteAsync(null));
        yield return new PaletteItem("Go to folder…", Glyphs.ChevronRight, "Ctrl+L", () => Do(() => b.ActivePane.BeginEditPathCommand.Execute(null)));
        yield return new PaletteItem(b.ShowHidden ? "Hide hidden files" : "Show hidden files", Glyphs.View, "Ctrl+H", () => Do(() => b.ToggleHiddenCommand.Execute(null)));
        yield return new PaletteItem(b.ShowLocalPane ? "Hide This PC" : "Show This PC", Glyphs.Panes, "", () => Do(() => b.ToggleLocalPaneCommand.Execute(null)));
        yield return new PaletteItem(b.ShowDetails ? "Hide details" : "Show details", Glyphs.Info, "", () => Do(() => b.ToggleDetailsCommand.Execute(null)));
        yield return new PaletteItem("Pin this folder to the sidebar", Glyphs.Pin, "", () => b.PinFolderCommand.ExecuteAsync(null));
        yield return new PaletteItem("Open terminal here", Glyphs.Terminal, "", () => Do(() => b.OpenTerminalCommand.Execute(null)));
        yield return new PaletteItem("Copy path", Glyphs.Copy, "", () => Do(() => b.CopyPathCommand.Execute(null)));
        yield return new PaletteItem("Show transfers", Glyphs.Sync, "", () => Do(() => b.Main.Transfers.IsOpen = true));
        yield return new PaletteItem("Settings", Glyphs.Settings, "Ctrl+,", () => Do(() => b.OpenSettingsCommand.Execute(null)));
        foreach (var s in b.Main.Connect.Cards.Where(c => c.Profile != b.Session.Profile))
            yield return new PaletteItem($"Connect to {s.Title}", Glyphs.Server, s.Profile.Address, () => b.Main.ConnectAsync(s));
        yield return new PaletteItem($"Disconnect from {b.ServerName}", Glyphs.Power, "", () => b.DisconnectCommand.ExecuteAsync(null));
    }
}
