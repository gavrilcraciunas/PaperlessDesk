using PaperlessDesktop.Dialogs;
using PaperlessDesktop.Interop;
using PaperlessDesktop.Services;
using PaperlessDesktop.ViewModels;

namespace PaperlessDesktop.Controls;

/// <summary>
/// Static helper methods shared by all per-file-type UserControls.
/// Each method takes the hosting Window (for pickers) and XamlRoot (for dialogs).
/// </summary>
internal static class ViewHelpers
{
    // ── Progress runner ───────────────────────────────────────────────────────

    public static async Task Run(XamlRoot root, string title,
        Func<IProgress<(int, int, string)>, CancellationToken, Task> work)
    {
        var dlg = new ProgressDialog(root, title);
        _ = dlg.ShowAsync();
        try
        {
            var prog = new Progress<(int d, int t, string n)>(x => dlg.Update(x.d, x.t, x.n));
            await work(prog, dlg.CancellationToken);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await Err(root, "Error", ex.Message); }
        finally { dlg.Hide(); }
    }

    // ── File / folder pickers ─────────────────────────────────────────────────

    public static async Task<string?> SaveFile(Window win, string suggested, string ext)
    {
        var p = new FileSavePicker();
        InitializeWithWindow.Initialize(p, WindowNative.GetWindowHandle(win));
        p.SuggestedFileName = suggested;
        p.FileTypeChoices.Add(ext.TrimStart('.').ToUpperInvariant(), new List<string> { ext });
        var result = await p.PickSaveFileAsync();
        return result?.Path;
    }

    public static async Task<string?> OpenFile(Window win, string ext)
    {
        var p = new FileOpenPicker();
        InitializeWithWindow.Initialize(p, WindowNative.GetWindowHandle(win));
        p.FileTypeFilter.Add(ext);
        return (await p.PickSingleFileAsync())?.Path;
    }

    public static async Task<string?> PickFolder(Window win)
    {
        var p = new FolderPicker();
        InitializeWithWindow.Initialize(p, WindowNative.GetWindowHandle(win));
        p.FileTypeFilter.Add("*");
        return (await p.PickSingleFolderAsync())?.Path;
    }

    // ── Dialog helpers ────────────────────────────────────────────────────────

    public static Task Info(XamlRoot root, string title, string msg) => new ContentDialog
    {
        Title = title,
        Content = msg,
        CloseButtonText = "OK",
        XamlRoot = root
    }.ShowAsync().AsTask();

    public static Task Err(XamlRoot root, string title, string msg) => new ContentDialog
    {
        Title = title,
        Content = msg,
        CloseButtonText = "OK",
        XamlRoot = root
    }.ShowAsync().AsTask();

    public static async Task<string?> Ask(XamlRoot root, string title, string prompt, string defaultValue = "")
    {
        var box = new TextBox { Text = defaultValue, PlaceholderText = prompt };
        var dlg = new ContentDialog
        {
            Title = title,
            Content = new StackPanel
            {
                Children = { new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap }, box }
            },
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            XamlRoot = root
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary ? box.Text : null;
    }

    public static async Task<string?> AskPassword(XamlRoot root, string title, string prompt)
    {
        var box = new PasswordBox { PlaceholderText = prompt };
        var dlg = new ContentDialog
        {
            Title = title,
            Content = new StackPanel
            {
                Children = { new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap }, box }
            },
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            XamlRoot = root
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary ? box.Password : null;
    }

    public static async Task<int?> AskInt(XamlRoot root, string title, string prompt, int defaultValue)
    {
        var box = new TextBox { Text = defaultValue.ToString() };
        var dlg = new ContentDialog
        {
            Title = title,
            Content = new StackPanel
            {
                Children = { new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap }, box }
            },
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            XamlRoot = root
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return null;
        return int.TryParse(box.Text, out int v) ? v : null;
    }

    public static async Task<string?> AskQuality(XamlRoot root)
    {
        var combo = new ComboBox
        {
            ItemsSource = new[] { "screen", "ebook", "printer", "prepress" },
            SelectedItem = "ebook",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var dlg = new ContentDialog
        {
            Title = "Compression Quality",
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Select quality preset:", TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "screen  – smallest file (72 dpi)", FontSize = 12, Opacity = 0.7 },
                    new TextBlock { Text = "ebook   – balanced (150 dpi)", FontSize = 12, Opacity = 0.7 },
                    new TextBlock { Text = "printer – high quality (300 dpi)", FontSize = 12, Opacity = 0.7 },
                    new TextBlock { Text = "prepress – maximum quality", FontSize = 12, Opacity = 0.7, Margin = new Thickness(0,0,0,8) },
                    combo
                }
            },
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            XamlRoot = root
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary
            ? combo.SelectedItem?.ToString()
            : null;
    }

    public static async Task<bool> YesNo(XamlRoot root, string title, string content,
        string yesLabel = "Yes", string noLabel = "No")
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = yesLabel,
            CloseButtonText = noLabel,
            XamlRoot = root
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary;
    }

    // ── Pro gate ──────────────────────────────────────────────────────────────

    public static bool RequirePro(XamlRoot root, LicenseService license, string feature)
    {
        if (license.IsPro) return true;
        _ = Info(root, "Pro Feature Required",
            $"{feature} is only available in the Pro version. Click Activate Pro in the top bar to unlock all features.");
        return false;
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    public static bool IsPdf(string path) =>
        path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    public static bool IsImage(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tiff" or ".webp";
    }

    public static bool IsDocx(string path) =>
        path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);

    public static bool IsCsvOrXlsx(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".csv" or ".xlsx";
    }
}
