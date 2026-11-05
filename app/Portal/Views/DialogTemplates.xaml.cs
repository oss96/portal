using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Portal.ViewModels;

namespace Portal.Views;

/// <summary>Event handlers for controls inside the dialog templates.</summary>
public partial class DialogTemplates : ResourceDictionary
{
    /// <summary>Creates the dictionary.</summary>
    public DialogTemplates() => InitializeComponent();

    private void OnSecretChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox { DataContext: SecretDialogViewModel vm } box) vm.Secret = box.Password;
    }

    private void OnSecretLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box) box.Dispatcher.InvokeAsync(() => Keyboard.Focus(box));
    }
}
