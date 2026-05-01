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

    private CancellationTokenSource? _statusCts;

    // ── Construction ──────────────────────────────────────────────────────────

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Mica backdrop for Windows 11 native feel
        if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
        {
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop
            {
                Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base
            };
        }

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

        // Set window icon (taskbar + title bar)
        var hwnd = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(
            WinRT.Interop.WindowNative.GetWindowHandle(this));
        var appWin = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(hwnd);
        appWin.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico"));
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

    // ── Settings ──────────────────────────────────────────────────────────────

    private async void SettingsBtn_Click(object s, RoutedEventArgs e)
    {
        var settings = App.Services.GetRequiredService<AppSettings>();
        var dlg = new SettingsDialog(Content.XamlRoot, this, settings);
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
        {
            dlg.ApplyTo(settings);
            settings.Save();
        }
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
        ClearBtn.IsEnabled  = ViewModel.Files.Count > 0;
        EmptyStatePanel.Visibility = ViewModel.Files.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
        SelectionCountBlock.Text = sel.Count > 0 ? $"{sel.Count} selected" : "";

        // Also refresh the active view's buttons
        if (_currentView == "pdf")   ViewPdf.RefreshButtons();
        if (_currentView == "docx")  ViewDocx.RefreshButtons();
        if (_currentView == "image") ViewImage.RefreshButtons();
    }

    // ── File list events ──────────────────────────────────────────────────────

    private async void AddBtn_Click(SplitButton s, SplitButtonClickEventArgs e)
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


    private void FileListView_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        var sel = FileListView.SelectedItems.Cast<FileItemViewModel>().ToList();
        var selected = sel.ToHashSet();
        foreach (var file in ViewModel.Files)
            file.IsSelected = selected.Contains(file);

        RefreshSidebarButtons();
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

    private async void AddFilesMenu_Click(object s, RoutedEventArgs e)
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
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await new ContentDialog { Title = "Error", Content = ex.Message,
                CloseButtonText = "OK", XamlRoot = Content.XamlRoot }.ShowAsync();
        }
    }

    // ── Add Folder ────────────────────────────────────────────────────────────

    private async void AddFolderBtn_Click(object s, RoutedEventArgs e)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, hwnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        var supported = new HashSet<string>(
            new[] { ".pdf", ".png", ".jpg", ".jpeg", ".docx", ".csv", ".xlsx" },
            StringComparer.OrdinalIgnoreCase);
        var paths = Directory.EnumerateFiles(folder.Path)
            .Where(p => supported.Contains(Path.GetExtension(p)));
        ViewModel.AddFiles(paths);
        RefreshSidebarButtons();
    }

    // ── Recent files ──────────────────────────────────────────────────────────

    private async void RecentFiles_Click(object s, RoutedEventArgs e)
    {
        var recent = App.Services.GetRequiredService<AppSettings>().RecentFiles
            .Where(File.Exists).Take(10).ToList();
        if (recent.Count == 0)
        {
            await new ContentDialog { Title = "Recent Files",
                Content = "No recent files found.",
                CloseButtonText = "OK", XamlRoot = Content.XamlRoot }.ShowAsync();
            return;
        }
        ViewModel.AddFiles(recent);
        RefreshSidebarButtons();
    }

    // ── Filter box ────────────────────────────────────────────────────────────

    private void FilterBox_TextChanged(object s, TextChangedEventArgs e)
    {
        var term = FilterBox.Text.Trim();
        foreach (var item in ViewModel.Files)
            item.IsFilteredOut = !string.IsNullOrEmpty(term) &&
                !item.FileName.Contains(term, StringComparison.OrdinalIgnoreCase);

        // Collapse filtered items via the ListView item containers
        for (int i = 0; i < ViewModel.Files.Count; i++)
        {
            var container = FileListView.ContainerFromIndex(i) as ListViewItem;
            if (container is not null)
                container.Visibility = ViewModel.Files[i].IsFilteredOut
                    ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    // ── Inline progress (replaces modal ProgressDialog) ───────────────────────

    /// <summary>Starts the inline status-bar progress. Returns a CancellationToken the operation should honour.</summary>
    public CancellationToken BeginOperation(string operationLabel)
    {
        _statusCts?.Cancel();
        _statusCts = new CancellationTokenSource();
        DispatcherQueue.TryEnqueue(() =>
        {
            InlineStatusLabel.Text       = operationLabel;
            InlineStatusLabel.Visibility = Visibility.Visible;
            StatusProgress.Value            = 0;
            StatusProgress.IsIndeterminate  = true;
            StatusProgress.Visibility       = Visibility.Visible;
            InlineCancelBtn.Visibility      = Visibility.Visible;
            StatusBlock.Text                = operationLabel;
        });
        return _statusCts.Token;
    }

    public void ReportProgress(int done, int total, string label)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            InlineStatusLabel.Text         = label;
            StatusProgress.IsIndeterminate = total <= 0;
            if (total > 0) { StatusProgress.Maximum = total; StatusProgress.Value = done; }
        });
    }

    public void EndOperation(string? finalMessage = null)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            InlineStatusLabel.Visibility = Visibility.Collapsed;
            StatusProgress.Visibility    = Visibility.Collapsed;
            InlineCancelBtn.Visibility   = Visibility.Collapsed;
            StatusBlock.Text             = finalMessage ?? "Ready.";
        });
    }

    private void StatusCancelBtn_Click(object s, RoutedEventArgs e)
    {
        _statusCts?.Cancel();
        EndOperation("Cancelled.");
    }

    // ── Features flyout handlers (delegate to the active view) ────────────────

    private void FlyMergePdf_Click(object s, RoutedEventArgs e)      => ViewPdf.InvokeMerge();
    private void FlySplitPdf_Click(object s, RoutedEventArgs e)      => ViewPdf.InvokeSplit();
    private void FlyRemovePages_Click(object s, RoutedEventArgs e)   => ViewPdf.InvokeRemovePages();
    private void FlyRotatePages_Click(object s, RoutedEventArgs e)   => ViewPdf.InvokeRotate();
    private void FlyInsertPages_Click(object s, RoutedEventArgs e)   => ViewPdf.InvokeInsert();
    private void FlyPassword_Click(object s, RoutedEventArgs e)      => ViewPdf.InvokePassword();
    private void FlyUnlock_Click(object s, RoutedEventArgs e)        => ViewPdf.InvokeUnlock();
    private void FlyMetadata_Click(object s, RoutedEventArgs e)      => ViewPdf.InvokeMetadata();
    private void FlyOcr_Click(object s, RoutedEventArgs e)           => ViewPdf.InvokeOcr();
    private void FlyPdfToDocx_Click(object s, RoutedEventArgs e)     => ViewPdf.InvokePdfToDocx();
    private void FlyPdfToImages_Click(object s, RoutedEventArgs e)   => ViewPdf.InvokePdfToImages();
    private void FlyCompress_Click(object s, RoutedEventArgs e)      => ViewPdf.InvokeCompress();
    private void FlyBatchCompress_Click(object s, RoutedEventArgs e) => ViewPdf.InvokeBatchCompress();
    private void FlyBatchOcr_Click(object s, RoutedEventArgs e)      => ViewPdf.InvokeBatchOcr();
    private void FlyBatchRemove_Click(object s, RoutedEventArgs e)   => ViewPdf.InvokeBatchRemove();
    private void FlyBatchRotate_Click(object s, RoutedEventArgs e)   => ViewPdf.InvokeBatchRotate();
    private void FlyBatchImages_Click(object s, RoutedEventArgs e)   => ViewPdf.InvokeBatchImages();
    private void FlyExportReport_Click(object s, RoutedEventArgs e)  => ViewPdf.InvokeExportReport();
    private void FlyResize_Click(object s, RoutedEventArgs e)        => ViewImage.InvokeResize();
    private void FlyCrop_Click(object s, RoutedEventArgs e)          => ViewImage.InvokeCrop();
    private void FlyRotateImg_Click(object s, RoutedEventArgs e)     => ViewImage.InvokeRotateCW();
    private void FlyGrayscale_Click(object s, RoutedEventArgs e)     => ViewImage.InvokeGrayscale();
    private void FlyImagesToPdf_Click(object s, RoutedEventArgs e)   => ViewImage.InvokeImagesToPdf();

    // ── Help menu handlers ────────────────────────────────────────────────────

    private async void HelpQuickStart_Click(object s, RoutedEventArgs e)
    {
        await new ContentDialog
        {
            Title = "Quick Start Guide",
            Content = new ScrollViewer
            {
                MaxHeight = 420,
                Content = new StackPanel { Spacing = 10, Children =
                {
                    new TextBlock { Text = "1. Add files",     FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    new TextBlock { Text = "Click \"Add Files\" or drag & drop PDF, image, Word, or spreadsheet files onto the list.", TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "2. Select a file", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    new TextBlock { Text = "Click any file in the sidebar to preview it and see available actions on the right.", TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "3. Run an action", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    new TextBlock { Text = "Choose an operation from the action panel (Pages, Security, Convert, Optimise, Batch tabs for PDFs) or from the Features menu in the top bar.", TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "4. Batch operations", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    new TextBlock { Text = "Select multiple PDFs and use the Batch tab to process them all at once. Progress is shown in the status bar at the bottom.", TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "5. Output folder", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    new TextBlock { Text = "Set a default output folder in Settings (⚙) to skip the Save dialog every time.", TextWrapping = TextWrapping.Wrap },
                }}
            },
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot
        }.ShowAsync();
    }

    private async void HelpShortcuts_Click(object s, RoutedEventArgs e)
    {
        await new ContentDialog
        {
            Title = "Keyboard Shortcuts",
            Content = new StackPanel { Spacing = 6, MinWidth = 340, Children =
            {
                MakeShortcutRow("Ctrl + O",      "Add files"),
                MakeShortcutRow("Delete",         "Remove selected file"),
                MakeShortcutRow("Ctrl + A",       "Select all files"),
                MakeShortcutRow("Ctrl + S",       "Save (DOCX / XLSX editor)"),
                MakeShortcutRow("Ctrl + F",       "Find & Replace (DOCX / XLSX)"),
                MakeShortcutRow("Ctrl + Z",       "Undo (DOCX editor)"),
                MakeShortcutRow("← / →",          "Previous / Next PDF page"),
                MakeShortcutRow("+  /  −",         "Zoom in / out (PDF preview)"),
                MakeShortcutRow("F1",              "Quick Start Guide"),
            }},
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot
        }.ShowAsync();
    }

    private static UIElement MakeShortcutRow(string keys, string desc)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var kb = new Border
        {
            Background  = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,240,240,240)),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(6,2,6,2),
            Child = new TextBlock { Text = keys, FontSize = 12, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") }
        };
        var txt = new TextBlock { Text = desc, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(kb,  0);
        Grid.SetColumn(txt, 1);
        grid.Children.Add(kb);
        grid.Children.Add(txt);
        return grid;
    }

    private async void HelpAbout_Click(object s, RoutedEventArgs e)
        => await new AboutDialog(Content.XamlRoot).ShowAsync();

    private async void HelpFeedback_Click(object s, RoutedEventArgs e)
        => await Windows.System.Launcher.LaunchUriAsync(
               new Uri($"mailto:{AppConstants.SupportEmail}?subject=Paperless Desktop Feedback"));

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
