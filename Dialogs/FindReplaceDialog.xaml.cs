using Microsoft.UI.
Xaml.Controls;
namespace PaperlessDesktop.Dialogs;

public sealed partial class FindReplaceDialog : ContentDialog
{
    public string FindText    => FindBox.Text;
    public string ReplaceText => ReplaceBox.Text;
    public bool   MatchCase   => MatchCaseToggle.IsOn;

    public FindReplaceDialog()
    {
        InitializeComponent();
    }
}