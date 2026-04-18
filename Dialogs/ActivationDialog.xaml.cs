namespace PaperlessDesktop.Dialogs;

public sealed partial class ActivationDialog : ContentDialog
{
    public string UserId => UserIdBox.Text.Trim();
    public string LicenseKey => KeyBox.Password.Trim();

    public ActivationDialog(Microsoft.UI.Xaml.XamlRoot root)
    { InitializeComponent(); XamlRoot = root; }
}
