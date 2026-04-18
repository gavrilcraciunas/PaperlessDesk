namespace PaperlessDesktop.Dialogs;

public sealed partial class MetadataDialog : ContentDialog
{
    public new string Title => TitleBox.Text;
    public string Author => AuthorBox.Text;
    public string Subject => SubjectBox.Text;
    public string Keywords => KeywordsBox.Text;

    public MetadataDialog(Microsoft.UI.Xaml.XamlRoot root)
    { InitializeComponent(); XamlRoot = root; }
}
