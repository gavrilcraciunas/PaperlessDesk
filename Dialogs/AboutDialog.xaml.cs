namespace PaperlessDesktop.Dialogs;

public sealed partial class AboutDialog : ContentDialog
{
    public AboutDialog(Microsoft.UI.Xaml.XamlRoot root)
    { InitializeComponent(); XamlRoot = root; }
}
