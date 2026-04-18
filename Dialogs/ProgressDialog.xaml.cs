namespace PaperlessDesktop.Dialogs;

public sealed partial class ProgressDialog : ContentDialog
{
    private readonly CancellationTokenSource _cts = new();
    public CancellationToken CancellationToken => _cts.Token;

    public ProgressDialog(Microsoft.UI.Xaml.XamlRoot root, string title)
    {
        InitializeComponent();
        XamlRoot = root;
        Title = title;
        CloseButtonClick += (_, _) => _cts.Cancel();
    }

    public void Update(int done, int total, string label)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ProgressBar.IsIndeterminate = total <= 0;
            if (total > 0) { ProgressBar.Maximum = total; ProgressBar.Value = done; }
            ProgressLabel.Text = label;
        });
    }
}
