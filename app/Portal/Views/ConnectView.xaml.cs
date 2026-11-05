using System.Windows.Controls;
using System.Windows.Input;
using Portal.ViewModels;

namespace Portal.Views;

/// <summary>Saved servers and the add/edit form.</summary>
public partial class ConnectView : UserControl
{
    /// <summary>Creates the view.</summary>
    public ConnectView() => InitializeComponent();

    private void OnFormKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not ConnectViewModel vm) return;
        if (sender is TextBox box) box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (vm.CanSave) vm.SaveCommand.Execute(null);
        e.Handled = true;
    }
}
