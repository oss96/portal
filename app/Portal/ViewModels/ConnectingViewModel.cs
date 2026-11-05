using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;

namespace Portal.ViewModels;

/// <summary>The browser layout shown while connecting from outside the connect screen, such as at startup.</summary>
public sealed partial class ConnectingViewModel
{
    /// <summary>Creates the screen for the server being connected to.</summary>
    public ConnectingViewModel(MainViewModel main, ServerCardViewModel card)
    {
        Main = main;
        Card = card;
        foreach (var s in main.Servers)
            Servers.Add(new SidebarItem(SidebarTarget.Server, s.Name, Glyphs.Server, s.Id) { IsConnected = main.IsOpen(s), IsCurrent = s == card.Profile });
    }

    /// <summary>App-wide state.</summary>
    public MainViewModel Main { get; }

    /// <summary>The server being connected to, with its progress.</summary>
    public ServerCardViewModel Card { get; }

    /// <summary>Saved servers.</summary>
    public ObservableCollection<SidebarItem> Servers { get; } = [];

    /// <summary>"Connecting to Tower".</summary>
    public string ConnectingText => $"Connecting to {Card.Title}";

    [RelayCommand]
    private void Cancel()
    {
        Card.CancelCommand.Execute(null);
        Main.LeaveConnecting(this);
    }

    [RelayCommand]
    private async Task OpenServer(SidebarItem item)
    {
        if (item.Path == Card.Profile.Id || Main.Connect.Cards.FirstOrDefault(c => c.Profile.Id == item.Path) is not { } other) return;
        Card.CancelCommand.Execute(null);
        await Main.ConnectAsync(other);
    }

    [RelayCommand]
    private void EditServer()
    {
        Main.ShowConnect();
        Main.Connect.BeginEdit(Card.Profile);
    }

    [RelayCommand]
    private void AddServer()
    {
        Card.CancelCommand.Execute(null);
        Main.ShowConnect();
    }

    [RelayCommand]
    private void OpenSettings() => Main.Updates.ShowSettings();
}
