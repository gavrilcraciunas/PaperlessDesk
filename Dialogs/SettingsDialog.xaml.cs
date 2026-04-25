using PaperlessDesktop.Interop;
using PaperlessDesktop.Shared;
using Windows.Storage.Pickers;

namespace PaperlessDesktop.Dialogs;

public sealed partial class SettingsDialog : ContentDialog
{
    private readonly AppSettings _settings;
    private readonly Window _win;

    public SettingsDialog(XamlRoot xamlRoot, Window win, AppSettings settings)
    {
        InitializeComponent();
        XamlRoot = xamlRoot;
        _settings = settings;
        _win = win;

        // Populate from current settings
        OutputDirBox.Text = settings.DefaultOutputDir;

        SelectComboByTag(QualityCombo, settings.DefaultQuality, "ebook");
        SelectComboByTag(OcrLangCombo, settings.DefaultOcrLang, "ron+eng");

        ShowPageCountToggle.IsOn = settings.ShowPageCount;
    }

    // ── Browse for output folder ──────────────────────────────────────────────

    private async void BrowseOutputBtn_Click(object s, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_win));
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
            OutputDirBox.Text = folder.Path;
    }

    // ── Apply (called by MainWindow after Primary button) ─────────────────────

    public void ApplyTo(AppSettings settings)
    {
        settings.DefaultOutputDir = OutputDirBox.Text.Trim();
        settings.DefaultQuality   = SelectedTag(QualityCombo)  ?? "ebook";
        settings.DefaultOcrLang   = SelectedTag(OcrLangCombo)  ?? "ron+eng";
        settings.ShowPageCount    = ShowPageCountToggle.IsOn;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void SelectComboByTag(ComboBox combo, string tag, string fallback)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (item.Tag as string == tag)
            {
                combo.SelectedItem = item;
                return;
            }
        }
        // fallback
        foreach (ComboBoxItem item in combo.Items)
        {
            if (item.Tag as string == fallback)
            {
                combo.SelectedItem = item;
                return;
            }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static string? SelectedTag(ComboBox combo)
        => (combo.SelectedItem as ComboBoxItem)?.Tag as string;
}
