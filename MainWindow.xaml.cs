using Microsoft.UI.Xaml.Media;
using PaperlessDesktop.Dialogs;
using PaperlessDesktop.Interop;
using PaperlessDesktop.ViewModels;
using System.Runtime.InteropServices;
using Windows.UI;

namespace PaperlessDesktop;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    // ── Construction ──────────────────────────────────────────────────────────

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        ExtendsContentIntoTitleBar = true;

        FileListView.ItemsSource = ViewModel.Files;

        // Wire status bar
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.StatusText))
                StatusBlock.Text = ViewModel.StatusText;
            if (e.PropertyName == nameof(ViewModel.FileCountText))
                FileCountBlock.Text = ViewModel.FileCountText;
        };

        ViewModel.Files.CollectionChanged += (_, _) => RefreshSidebarButtons();

        // Give each view its VM + window reference
        ViewPdf.Initialize(ViewModel, this);
        ViewDocx.Initialize(ViewModel, this);
        ViewImage.Initialize(ViewModel, this);
        ViewXlsx.Initialize(ViewModel, this);

        UpdateLicenseBadge();
        RefreshSidebarButtons();
        ShowView("empty");

        // Restore dark mode preference from settings
        ApplyTheme(ViewModel.IsDarkMode);
    }

    // ── License badge ─────────────────────────────────────────────────────────

    private void UpdateLicenseBadge()
    {
        LicenseStatusBlock.Text = ViewModel.License.LicenseStatusText;

        if (ViewModel.License.IsActivated)
        {
            LicenseBadge.Background = new SolidColorBrush(Color.FromArgb(255, 16, 124, 16));
            LicenseStatusBlock.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
            ActivateBtn.Visibility = Visibility.Collapsed;
        }
        else if (ViewModel.License.TrialDaysLeft > 0)
        {
            LicenseBadge.Background = new SolidColorBrush(Color.FromArgb(30, 200, 146, 42));
            LicenseStatusBlock.Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 90, 0));
        }
        else
        {
            LicenseBadge.Background = new SolidColorBrush(Color.FromArgb(30, 196, 43, 28));
            LicenseStatusBlock.Foreground = new SolidColorBrush(Color.FromArgb(255, 196, 43, 28));
        }
    }

    // ── Dark mode ─────────────────────────────────────────────────────────────

    private void DarkModeBtn_Click(object s, RoutedEventArgs e)
    {
        ViewModel.IsDarkMode = !ViewModel.IsDarkMode;
        ApplyTheme(ViewModel.IsDarkMode);
    }

    private void ApplyTheme(bool dark)
    {
        if (Content is FrameworkElement root)
            root.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        DarkModeIcon.Glyph = dark ? "\uE706" : "\uE708"; // sun : moon
    }

    // ── View switching ────────────────────────────────────────────────────────

    private string _currentView = "empty";

    private void ShowView(string view)
    {
        _currentView = view;
        ViewEmpty.Visibility = view == "empty"  ? Visibility.Visible : Visibility.Collapsed;
        ViewPdf.Visibility   = view == "pdf"    ? Visibility.Visible : Visibility.Collapsed;
        ViewDocx.Visibility  = view == "docx"   ? Visibility.Visible : Visibility.Collapsed;
        ViewImage.Visibility = view == "image"  ? Visibility.Visible : Visibility.Collapsed;
        ViewXlsx.Visibility  = view == "xlsx"   ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Sidebar button states ─────────────────────────────────────────────────

    private void RefreshSidebarButtons()
    {
        var sel = FileListView.SelectedItems.Cast<FileItemViewModel>().ToList();
        RemoveBtn.IsEnabled = sel.Count > 0;
        ClearBtn.IsEnabled = ViewModel.Files.Count > 0;
        MoveUpBtn.IsEnabled = sel.Count == 1 && ViewModel.Files.IndexOf(sel[0]) > 0;
        MoveDownBtn.IsEnabled = sel.Count == 1 &&
            ViewModel.Files.IndexOf(sel[0]) < ViewModel.Files.Count - 1;
        EmptyStatePanel.Visibility = ViewModel.Files.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
        SelectionCountBlock.Text = sel.Count > 0 ? $"{sel.Count} selected" : "";

        // Also refresh the active view's buttons
        if (_currentView == "pdf")   ViewPdf.RefreshButtons();
        if (_currentView == "docx")  ViewDocx.RefreshButtons();
        if (_currentView == "image") ViewImage.RefreshButtons();
    }

    // ── File list events ──────────────────────────────────────────────────────

    private async void AddBtn_Click(object s, RoutedEventArgs e)
    {
        try
        {
            var files = await PickMultipleFiles();
            if (files?.Count > 0)
            {
                var existing = ViewModel.Files.Select(f => f.FilePath)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var newPaths = files.Select(f => f.Path).Where(p => !existing.Contains(p));
                ViewModel.AddFiles(newPaths);
                RefreshSidebarButtons();
            }
        }
        catch (COMException comEx) when (comEx.HResult == unchecked((int)0x80070578))
        {
            await new ContentDialog { Title = "File Picker Error",
                Content = "Invalid window handle. Please try restarting the app.",
                CloseButtonText = "OK", XamlRoot = Content.XamlRoot }.ShowAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await new ContentDialog { Title = "Error", Content = ex.Message,
                CloseButtonText = "OK", XamlRoot = Content.XamlRoot }.ShowAsync();
        }
    }

    private async Task<IReadOnlyList<StorageFile>?> PickMultipleFiles()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        try
        {
            var picker = new FileOpenPicker { ViewMode = PickerViewMode.List,
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            foreach (var ext in new[] { ".pdf", ".png", ".jpg", ".jpeg", ".docx", ".csv", ".xlsx" })
                picker.FileTypeFilter.Add(ext);
            if (hwnd != IntPtr.Zero) InitializeWithWindow.Initialize(picker, hwnd);
            return await picker.PickMultipleFilesAsync();
        }
        catch (COMException comEx) when (comEx.HResult == unchecked((int)0x80070578))
        {
            if (hwnd == IntPtr.Zero) throw;
            var filter = "All Supported|*.pdf;*.png;*.jpg;*.jpeg;*.docx;*.csv;*.xlsx|PDF Files|*.pdf|Image Files|*.png;*.jpg;*.jpeg|Document Files|*.docx;*.csv;*.xlsx|All Files|*.*";
            var filePaths = Win32FileDialog.OpenFileDialog(hwnd, "Select Files to Add", filter);
            if (filePaths.Length == 0) return null;
            var result = new List<StorageFile>();
            foreach (var path in filePaths)
            {
                try { result.Add(await StorageFile.GetFileFromPathAsync(path)); }
                catch { }
            }
            return result;
        }
    }

    private void RemoveBtn_Click(object s, RoutedEventArgs e)
    {
        foreach (var item in FileListView.SelectedItems.Cast<FileItemViewModel>().ToList())
            ViewModel.RemoveFile(item);
    }

    private async void ClearBtn_Click(object s, RoutedEventArgs e)
    {
        var dlg = new ContentDialog { Title = "Clear list",
            Content = "Remove all files from the list?",
            PrimaryButtonText = "Yes", CloseButtonText = "No",
            XamlRoot = Content.XamlRoot };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.ClearFiles();
            ShowView("empty");
        }
    }

    private void MoveUpBtn_Click(object s, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is FileItemViewModel item)
        { ViewModel.MoveUp(item); RefreshSidebarButtons(); }
    }

    private void MoveDownBtn_Click(object s, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is FileItemViewModel item)
        { ViewModel.MoveDown(item); RefreshSidebarButtons(); }
    }

    private void FileListView_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        var selected = FileListView.SelectedItems.Cast<FileItemViewModel>().ToHashSet();
        foreach (var file in ViewModel.Files)
            file.IsSelected = selected.Contains(file);

        RefreshSidebarButtons();

        var sel = selected.ToList();
        if (sel.Count == 1)
        {
            var path = sel[0].FilePath;
            if (IsPdf(path))
            {
                ShowView("pdf");
                ViewPdf.LoadFile(path);
            }
            else if (IsDocx(path))
            {
                ShowView("docx");
                ViewDocx.LoadFile(path);
            }
            else if (IsImage(path))
            {
                ShowView("image");
                ViewImage.LoadFile(path);
            }
            else if (IsCsvOrXlsx(path))
            {
                ShowView("xlsx");
                ViewXlsx.LoadFile(path);
            }
            else
            {
                ShowView("empty");
            }
        }
        else if (sel.Count == 0)
        {
            ShowView("empty");
            ViewDocx.Clear();
            ViewXlsx.Clear();
            ViewImage.Clear();
            ViewPdf.Clear();
        }
        else
        {
            // Multiple selected — show PDF view (for batch ops), clear preview
            if (sel.All(f => IsPdf(f.FilePath)))
            {
                ShowView("pdf");
                ViewPdf.Clear();
                ViewPdf.RefreshButtons();
            }
            else if (sel.All(f => IsImage(f.FilePath)))
            {
                ShowView("image");
                ViewImage.Clear();
                ViewImage.RefreshButtons();
            }
            else if (sel.All(f => IsDocx(f.FilePath)))
            {
                ShowView("docx");
                ViewDocx.Clear();
                ViewDocx.RefreshButtons();
            }
            else
            {
                ShowView("empty");
            }
        }
    }

    private void FileListView_DragOver(object s, DragEventArgs e)
        => e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;

    private async void FileListView_Drop(object s, DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var existing = ViewModel.Files.Select(f => f.FilePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            ViewModel.AddFiles(items.Select(i => i.Path).Where(p => !existing.Contains(p)));
        }
    }

    private void FileListView_DoubleTapped(object s, DoubleTappedRoutedEventArgs e)
    {
        if (FileListView.SelectedItem is FileItemViewModel item)
            Process.Start(new ProcessStartInfo(item.FilePath) { UseShellExecute = true });
    }

    // ── Activate Pro ──────────────────────────────────────────────────────────

    private async void ActivateBtn_Click(object s, RoutedEventArgs e)
    {
        var dlg = new ActivationDialog(Content.XamlRoot);
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
        {
            if (ViewModel.License.Activate(dlg.UserId, dlg.LicenseKey))
            {
                UpdateLicenseBadge();
                await new ContentDialog { Title = "Pro Activated! 🎉",
                    Content = "All Pro features are now unlocked.",
                    CloseButtonText = "OK", XamlRoot = Content.XamlRoot }.ShowAsync();
            }
            else
            {
                await new ContentDialog { Title = "Invalid Key",
                    Content = "That license key is not valid. Please check the key and try again.",
                    CloseButtonText = "OK", XamlRoot = Content.XamlRoot }.ShowAsync();
            }
        }
    }

    // ── File type helpers ─────────────────────────────────────────────────────

    private static bool IsPdf(string path) =>
        path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    private static bool IsImage(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tiff" or ".webp";
    }

    private static bool IsDocx(string path) =>
        path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);

    private static bool IsCsvOrXlsx(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".csv" or ".xlsx";
    }


}
