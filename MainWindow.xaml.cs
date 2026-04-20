using Microsoft.UI.Xaml.Media;
using PaperlessDesktop.Dialogs;
using PaperlessDesktop.Interop;
using PaperlessDesktop.Services;
using PaperlessDesktop.ViewModels;
using System.Runtime.InteropServices;
using Windows.UI;

namespace PaperlessDesktop;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    // ── Construction ─────────────────────────────────────────────────────────

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

        // Wire file list changes to button state refresh
        ViewModel.Files.CollectionChanged += (_, _) => RefreshButtonStates();

        // Initial state
        UpdateLicenseBadge();
        RefreshButtonStates();
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

    // ── Button state management ───────────────────────────────────────────────

    private void RefreshButtonStates()
    {
        var sel = FileListView.SelectedItems.Cast<FileItemViewModel>().ToList();
        var selPdfs = sel.Where(f => IsPdf(f.FilePath)).ToList();
        var selImages = sel.Where(f => IsImage(f.FilePath)).ToList();
        var allPdfs = ViewModel.Files.Where(f => IsPdf(f.FilePath)).ToList();
        bool hasPdfs = allPdfs.Count > 0;
        bool oneSelPdf = selPdfs.Count == 1;
        bool manySelPdf = selPdfs.Count >= 2;
        bool anySelImg = selImages.Count >= 1;
        bool anySel = sel.Count >= 1;

        // Sidebar controls
        RemoveBtn.IsEnabled = anySel;
        ClearBtn.IsEnabled = ViewModel.Files.Count > 0;
        MoveUpBtn.IsEnabled = anySel && sel.Count == 1 &&
                                ViewModel.Files.IndexOf(sel[0]) > 0;
        MoveDownBtn.IsEnabled = anySel && sel.Count == 1 &&
                                ViewModel.Files.IndexOf(sel[0]) < ViewModel.Files.Count - 1;

        // Empty state panel
        EmptyStatePanel.Visibility = ViewModel.Files.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;

        // PDF tab
        BtnMerge.IsEnabled = manySelPdf;
        BtnRemovePages.IsEnabled = oneSelPdf;
        BtnRotate.IsEnabled = oneSelPdf;
        BtnInsert.IsEnabled = oneSelPdf;
        BtnPassword.IsEnabled = oneSelPdf;
        BtnUnlock.IsEnabled = oneSelPdf;
        BtnMetadata.IsEnabled = oneSelPdf;

        // Pro tab
        BtnOcr.IsEnabled = oneSelPdf;
        BtnToDocx.IsEnabled = oneSelPdf;
        BtnToImages.IsEnabled = oneSelPdf;
        BtnFromImages.IsEnabled = anySelImg;
        BtnCompress.IsEnabled = oneSelPdf;
        BtnSplit.IsEnabled = oneSelPdf;

        // Batch tab
        BtnBatchCompress.IsEnabled = hasPdfs;
        BtnBatchOcr.IsEnabled = hasPdfs;
        BtnBatchRemove.IsEnabled = hasPdfs;
        BtnBatchRotate.IsEnabled = hasPdfs;
        BtnBatchToImages.IsEnabled = hasPdfs;
        BtnExportReport.IsEnabled = ViewModel.LastBatchResult is not null;

        // Selection status
        SelectionCountBlock.Text = sel.Count > 0 ? $"{sel.Count} selected" : "";
    }

    private static bool IsPdf(string path) =>
        path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    private static bool IsImage(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tiff" or ".webp";
    }

    // ── File list events ──────────────────────────────────────────────────────

    private async void AddBtn_Click(object s, RoutedEventArgs e)
    {
        try
        {
            Logger.Info("AddBtn_Click: Starting file picker");
            var files = await PickMultipleFiles();
            if (files?.Count > 0)
            {
                Logger.Info($"Adding {files.Count} files to view model");
                // FIX: filter out duplicates already in the list
                var existing = ViewModel.Files.Select(f => f.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var newPaths = files.Select(f => f.Path).Where(p => !existing.Contains(p));
                ViewModel.AddFiles(newPaths);
                RefreshButtonStates();
                Logger.Info("Files added successfully");
            }
        }
        catch (COMException comEx) when (comEx.HResult == unchecked((int)0x80070578))
        {
            Logger.Error($"COMException 0x80070578: {comEx}");
            await Err("File Picker Error", "Invalid window handle. Please try restarting the app.");
        }
        catch (OperationCanceledException)
        {
            Logger.Info("File picker operation cancelled by user");
        }
        catch (Exception ex)
        {
            Logger.Error($"AddBtn_Click error: {ex}");
            await Err("Error", $"An error occurred: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens the WinRT file picker. Falls back to native Win32 dialog if the
    /// WinRT picker fails due to an invalid window handle (common in WinUI 3).
    /// </summary>
    private async Task<IReadOnlyList<StorageFile>?> PickMultipleFiles()
    {
        Logger.Info("PickMultipleFiles: Starting");
        var hwnd = WindowNative.GetWindowHandle(this);
        Logger.Info($"Window handle: {hwnd}");

        try
        {
            Logger.Info("Attempting WinRT FileOpenPicker…");
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.List,
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            foreach (var ext in new[] { ".pdf", ".png", ".jpg", ".jpeg", ".docx", ".csv", ".xlsx" })
                picker.FileTypeFilter.Add(ext);

            if (hwnd != IntPtr.Zero)
                InitializeWithWindow.Initialize(picker, hwnd);

            var files = await picker.PickMultipleFilesAsync();
            Logger.Info($"✓ WinRT picker returned {files?.Count ?? 0} files");
            return files;
        }
        catch (COMException comEx) when (comEx.HResult == unchecked((int)0x80070578))
        {
            Logger.Warn("WinRT picker failed (0x80070578) — falling back to Win32 dialog");

            if (hwnd == IntPtr.Zero)
            {
                Logger.Error("No valid HWND for Win32 fallback");
                throw;
            }

            var filter = "PDF Files|*.pdf|Image Files|*.png;*.jpg;*.jpeg|Document Files|*.docx;*.csv;*.xlsx|All Files|*.*";
            var filePaths = Win32FileDialog.OpenFileDialog(hwnd, "Select Files to Add", filter);

            if (filePaths.Length == 0)
            {
                Logger.Info("Win32 dialog cancelled");
                return null;
            }

            Logger.Info($"Win32 dialog returned {filePaths.Length} file(s)");
            var result = new List<StorageFile>();
            foreach (var path in filePaths)
            {
                try { result.Add(await StorageFile.GetFileFromPathAsync(path)); }
                catch (Exception ex) { Logger.Warn($"Could not load {path}: {ex.Message}"); }
            }
            Logger.Info($"✓ Converted {result.Count} file(s) to StorageFile");
            return result;
        }
        catch (Exception ex)
        {
            Logger.Error($"PickMultipleFilesAsync failed: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    private void RemoveBtn_Click(object s, RoutedEventArgs e)
    {
        foreach (var item in FileListView.SelectedItems.Cast<FileItemViewModel>().ToList())
            ViewModel.RemoveFile(item);
    }

    private async void ClearBtn_Click(object s, RoutedEventArgs e)
    {
        var dlg = new ContentDialog
        {
            Title = "Clear list",
            Content = "Remove all files from the list?",
            PrimaryButtonText = "Yes",
            CloseButtonText = "No",
            XamlRoot = Content.XamlRoot
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            ViewModel.ClearFiles();
    }

    private void MoveUpBtn_Click(object s, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is FileItemViewModel item)
        {
            ViewModel.MoveUp(item);
            RefreshButtonStates();
        }
    }

    private void MoveDownBtn_Click(object s, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is FileItemViewModel item)
        {
            ViewModel.MoveDown(item);
            RefreshButtonStates();
        }
    }

    private void FileListView_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        // Sync IsSelected on each FileItemViewModel with the ListView selection
        var selected = FileListView.SelectedItems.Cast<FileItemViewModel>().ToHashSet();
        foreach (var file in ViewModel.Files)
            file.IsSelected = selected.Contains(file);

        RefreshButtonStates();
        var sel = selected.ToList();
        if (sel.Count == 1 && IsPdf(sel[0].FilePath))
            Preview.LoadPdf(sel[0].FilePath);
        else if (sel.Count == 0)
            Preview.Clear();
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
            var newPaths = items.Select(i => i.Path).Where(p => !existing.Contains(p));
            ViewModel.AddFiles(newPaths);
        }
    }

    // ── PDF Actions ───────────────────────────────────────────────────────────

    private async void MergePdfs_Click(object s, RoutedEventArgs e)
    {
        var sel = ViewModel.SelectedPdfs.ToList();
        if (sel.Count < 2) { await Info("Merge PDFs", "Select at least 2 PDF files."); return; }
        var out_ = await SaveFile("merged.pdf", ".pdf"); if (out_ is null) return;
        await Run("Merging PDFs…", (_, _) =>
        {
            App.Services.GetRequiredService<PdfCoreService>()
                        .MergePdfs(sel.Select(f => f.FilePath), out_);
            return Task.CompletedTask;
        });
        await Info("Merge Complete", $"✓  {sel.Count} files merged.\n{out_}");
    }

    private async void RemovePages_Click(object s, RoutedEventArgs e)
    {
        var sel = ViewModel.SelectedPdfs.FirstOrDefault(); if (sel is null) return;
        var spec = await Ask("Remove Pages", "Enter pages to remove:Examples:  3   |   1,3,5   |   2-4   |   1,3-5,7");
        if (spec is null) return;
        var out_ = await SaveFile($"{Path.GetFileNameWithoutExtension(sel.FilePath)}_pages_removed.pdf", ".pdf");
        if (out_ is null) return;
        PdfCoreService.RemoveResult? removeResult = null;
        await Run("Removing pages…", (_, _) =>
        {
            removeResult = App.Services.GetRequiredService<PdfCoreService>()
                                  .RemovePages(sel.FilePath, out_, spec);
            return Task.CompletedTask;
        });
        if (removeResult is not null)
            await Info("Done", $"✓  Removed {removeResult.RemovedCount} page(s).\n{out_}");
    }

    private async void RotatePages_Click(object s, RoutedEventArgs e)
    {
        var sel = ViewModel.SelectedPdfs.FirstOrDefault(); if (sel is null) return;
        var spec = await Ask("Rotate Pages", "Pages to rotate (e.g. 1,3-5  or  all):", "all");
        if (spec is null) return;
        var angle = await AskInt("Rotation Angle", "Degrees (90 / 180 / 270):", 90);
        if (angle is null) return;
        if (angle.Value % 90 != 0 || angle.Value is 0 or 360)
        {
            await Err("Invalid Angle", "Rotation must be 90, 180, or 270 degrees.");
            return;
        }
        var out_ = await SaveFile($"{Path.GetFileNameWithoutExtension(sel.FilePath)}_rotated.pdf", ".pdf");
        if (out_ is null) return;
        await Run("Rotating pages…", (_, _) =>
        {
            App.Services.GetRequiredService<PdfCoreService>()
                        .RotatePages(sel.FilePath, out_, spec, angle.Value);
            return Task.CompletedTask;
        });
        await Info("Done", $"✓  Pages rotated {angle}°.\n{out_}");
    }

    private async void InsertPages_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Insert Pages")) return;
        var sel = ViewModel.SelectedPdfs.FirstOrDefault(); if (sel is null) return;
        var src = await OpenFile(".pdf"); if (src is null) return;
        var after = await AskInt("Insert Position", "Insert after page number (0 = beginning):", 0);
        if (after is null) return;
        var out_ = await SaveFile($"{Path.GetFileNameWithoutExtension(sel.FilePath)}_inserted.pdf", ".pdf");
        if (out_ is null) return;
        await Run("Inserting pages…", (_, _) =>
        {
            App.Services.GetRequiredService<PdfCoreService>()
                        .InsertPages(sel.FilePath, src, after.Value, out_);
            return Task.CompletedTask;
        });
        await Info("Done", $"✓  Pages inserted.\n{out_}");
    }

    // FIX: PasswordProtect — now fully implemented via PdfCoreService (iText7)
    private async void PasswordProtect_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Password Protect")) return;
        var sel = ViewModel.SelectedPdfs.FirstOrDefault(); if (sel is null) return;

        var userPwd = await AskPassword("User Password",
            "Password users need to open the document:");
        if (userPwd is null) return;

        var ownerPwd = await AskPassword("Owner Password",
            "Owner password (controls permissions — can be the same):");
        if (ownerPwd is null) return;

        var out_ = await SaveFile($"{Path.GetFileNameWithoutExtension(sel.FilePath)}_protected.pdf", ".pdf");
        if (out_ is null) return;

        await Run("Encrypting PDF…", (_, _) =>
        {
            App.Services.GetRequiredService<PdfCoreService>()
                        .PasswordProtect(sel.FilePath, out_, userPwd, ownerPwd);
            return Task.CompletedTask;
        });
        await Info("Done", $"✓  PDF is now password-protected.\n{out_}");
    }

    // FIX: UnlockPdf — now fully implemented via PdfCoreService (iText7)
    private async void UnlockPdf_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Unlock PDF")) return;
        var sel = ViewModel.SelectedPdfs.FirstOrDefault(); if (sel is null) return;

        var pwd = await AskPassword("PDF Password", "Enter the current password to unlock:");
        if (pwd is null) return;

        var out_ = await SaveFile($"{Path.GetFileNameWithoutExtension(sel.FilePath)}_unlocked.pdf", ".pdf");
        if (out_ is null) return;

        await Run("Unlocking PDF…", (_, _) =>
        {
            App.Services.GetRequiredService<PdfCoreService>()
                        .UnlockPdf(sel.FilePath, out_, pwd);
            return Task.CompletedTask;
        });
        await Info("Done", $"✓  Password removed.\n{out_}");
    }

    private async void EditMetadata_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Edit Metadata")) return;
        var sel = ViewModel.SelectedPdfs.FirstOrDefault(); if (sel is null) return;
        var title = await Ask("Title", "Document title:", ""); if (title is null) return;
        var author = await Ask("Author", "Author name:", ""); if (author is null) return;
        var subject = await Ask("Subject", "Subject:", ""); if (subject is null) return;
        var keys = await Ask("Keywords", "Keywords:", ""); if (keys is null) return;
        var out_ = await SaveFile($"{Path.GetFileNameWithoutExtension(sel.FilePath)}_meta.pdf", ".pdf");
        if (out_ is null) return;
        await Run("Updating metadata…", (_, _) =>
        {
            App.Services.GetRequiredService<PdfCoreService>().EditMetadata(sel.FilePath, out_, title, author, subject, keys);
            return Task.CompletedTask;
        });
        await Info("Done", $"✓  Metadata updated.\n{out_}");
    }

    // ── Pro Actions ───────────────────────────────────────────────────────────

    private async void OcrPdf_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("OCR PDF")) return;
        var sel = ViewModel.SelectedPdfs.FirstOrDefault(); if (sel is null) return;
        var out_ = await SaveFile($"{Path.GetFileNameWithoutExtension(sel.FilePath)}_ocr.pdf", ".pdf");
        if (out_ is null) return;
        OcrResult? ocrResult = null;
        await Run("Running OCR…", async (prog, ct) =>
        {
            ocrResult = await App.Services.GetRequiredService<PdfConvertService>()
                               .OcrPdfAsync(sel.FilePath, out_, progress: prog, ct: ct);
        });
        if (ocrResult is not null)
            await Info("OCR Complete", $"✓  {ocrResult.PagesProcessed} page(s) processed.\n{out_}");
    }

    private async void PdfToDocx_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("PDF → Word")) return;
        var sel = ViewModel.SelectedPdfs.FirstOrDefault(); if (sel is null) return;
        var out_ = await SaveFile($"{Path.GetFileNameWithoutExtension(sel.FilePath)}.docx", ".docx");
        if (out_ is null) return;
        await Run("Converting to Word…", async (_, ct) =>
        {
            await App.Services.GetRequiredService<PdfConvertService>()
                               .PdfToDocxAsync(sel.FilePath, out_, ct: ct);
        });
        await Info("Conversion Complete", $"✓  Saved as Word document.\n{out_}");
    }

    private async void PdfToImages_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("PDF → Images")) return;
        var sel = ViewModel.SelectedPdfs.FirstOrDefault(); if (sel is null) return;
        var outDir = await PickFolder(); if (outDir is null) return;
        List<string>? exportedImages = null;
        await Run("Exporting pages as images…", async (prog, ct) =>
        {
            exportedImages = await App.Services.GetRequiredService<PdfConvertService>()
                                .PdfToImagesAsync(sel.FilePath, outDir, progress: prog, ct: ct);
        });
        if (exportedImages is not null)
            await Info("Export Complete", $"✓  {exportedImages.Count} image(s) saved to:\n{outDir}");
    }

    private async void ImagesToPdf_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Images → PDF")) return;
        var images = ViewModel.SelectedImages.ToList();
        if (images.Count == 0) { await Info("Images → PDF", "Select at least one image from the list."); return; }
        var out_ = await SaveFile("images.pdf", ".pdf"); if (out_ is null) return;
        ImagesToPdfResult? imgResult = null;
        await Run($"Creating PDF from {images.Count} image(s)…", async (prog, ct) =>
        {
            imgResult = await App.Services.GetRequiredService<PdfConvertService>()
                               .ImagesToPdfAsync(images.Select(f => f.FilePath), out_, progress: prog, ct: ct);
        });
        if (imgResult is not null)
            await Info("Done", $"✓  PDF created from {imgResult.ImageCount} image(s).\n{out_}");
    }

    private async void CompressPdf_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Compress PDF")) return;
        var sel = ViewModel.SelectedPdfs.FirstOrDefault(); if (sel is null) return;
        var quality = await AskQuality(); if (quality is null) return;
        var out_ = await SaveFile($"{Path.GetFileNameWithoutExtension(sel.FilePath)}_compressed.pdf", ".pdf");
        if (out_ is null) return;
        PdfCoreService.CompressResult? compressResult = null;
        await Run("Compressing PDF…", (_, _) =>
        {
            compressResult = App.Services.GetRequiredService<PdfCoreService>().CompressPdf(sel.FilePath, out_, quality!);
            return Task.CompletedTask;
        });
        if (compressResult is not null)
        {
            var msg = compressResult.KeptOriginal
                ? $"File was already optimal — original quality preserved.\nSize: {FormatBytes(compressResult.OutputSize)}"
                : $"✓  {FormatBytes(compressResult.OriginalSize)}  →  {FormatBytes(compressResult.OutputSize)}  ({compressResult.SavingsPct:F1}% smaller)";
            await Info("Compression Complete", msg + $"\n{out_}");
        }
    }

    private async void SplitPdf_Click(object sender, RoutedEventArgs e)
    {
        if (!RequirePro("Split PDF"))
            return;

        var selected = ViewModel.SelectedPdfs.FirstOrDefault();
        if (selected is null)
            return;

        var outputDir = await PickFolder();
        if (outputDir is null)
            return;

        bool splitByN = await YesNo(
            "Split Method",
            "How would you like to split this PDF?",
            "Yes = every N pages",
            "No = custom page ranges"
        );

        var pdfService = App.Services.GetRequiredService<PdfCoreService>();
        PdfCoreService.SplitResult result;

        if (splitByN)
        {
            var pagesPerChunk = await AskInt("Pages per chunk", "Split every N pages:", 2);
            if (pagesPerChunk is null)
                return;

            result = pdfService.SplitEveryN(
                selected.FilePath,
                outputDir,
                pagesPerChunk.Value
            );
        }
        else
        {
            var rangeSpec = await Ask(
                "Page Ranges",
                "Enter ranges (e.g. 1-3, 5, 7-9):"
            );

            if (rangeSpec is null)
                return;

            result = pdfService.SplitByRanges(
                selected.FilePath,
                outputDir,
                rangeSpec
            );
        }

        await Info(
            "Split Complete",
            $"✓ Created {result.OutputFiles.Count} file(s) in: {outputDir}"
        );
    }

    private async Task<bool> YesNo(string title, string content, string yesLabel, string noLabel)
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = yesLabel,
            CloseButtonText = noLabel,
            XamlRoot = Content.XamlRoot
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary;
    }

    // ── Batch Actions ─────────────────────────────────────────────────────────

    private async void BatchCompress_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Batch Compress")) return;
        var quality = await AskQuality(); if (quality is null) return;
        var files = ViewModel.AllOrSelected().Select(f => f.FilePath).ToList();
        var outDir = await PickFolder(); if (outDir is null) return;
        BatchResult? batchResult = null;
        await Run($"Compressing {files.Count} file(s)…", async (prog, ct) =>
        {
            batchResult = await App.Services.GetRequiredService<BatchService>()
                               .BatchCompressAsync(files, outDir, quality, prog, ct);
        });
        if (batchResult is not null)
        {
            ViewModel.LastBatchResult = batchResult;
            RefreshButtonStates();
            await Info("Batch Complete", batchResult.Summary());
        }
    }

    private async void BatchOcr_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Batch OCR")) return;
        var files = ViewModel.AllOrSelected().Select(f => f.FilePath).ToList();
        var outDir = await PickFolder(); if (outDir is null) return;
        BatchResult? batchResult = null;
        await Run($"OCR processing {files.Count} file(s)…", async (prog, ct) =>
        {
            batchResult = await App.Services.GetRequiredService<BatchService>()
                               .BatchOcrAsync(files, outDir, prog: prog, ct: ct);
        });
        if (batchResult is not null)
        {
            ViewModel.LastBatchResult = batchResult;
            RefreshButtonStates();
            await Info("Batch Complete", batchResult.Summary());
        }
    }

    private async void BatchRemove_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Batch Remove Pages")) return;
        var spec = await Ask("Remove Pages", "Pages to remove from ALL files (e.g. 1,3-5):"); if (spec is null) return;
        var files = ViewModel.AllOrSelected().Select(f => f.FilePath).ToList();
        var outDir = await PickFolder(); if (outDir is null) return;
        BatchResult? batchResult = null;
        await Run($"Removing pages from {files.Count} file(s)…", async (prog, ct) =>
        {
            batchResult = await App.Services.GetRequiredService<BatchService>()
                               .BatchRemovePagesAsync(files, outDir, spec, prog, ct);
        });
        if (batchResult is not null)
        {
            ViewModel.LastBatchResult = batchResult;
            RefreshButtonStates();
            await Info("Batch Complete", batchResult.Summary());
        }
    }

    private async void BatchRotate_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Batch Rotate")) return;
        var spec = await Ask("Pages", "Pages to rotate (all or e.g. 1,3-5):", "all"); if (spec is null) return;
        var angle = await AskInt("Angle", "Rotation angle (90/180/270):", 90); if (angle is null) return;
        var files = ViewModel.AllOrSelected().Select(f => f.FilePath).ToList();
        var outDir = await PickFolder(); if (outDir is null) return;
        BatchResult? batchResult = null;
        await Run($"Rotating {files.Count} file(s)…", async (prog, ct) =>
        {
            batchResult = await App.Services.GetRequiredService<BatchService>()
                               .BatchRotateAsync(files, outDir, spec, angle.Value, prog, ct);
        });
        if (batchResult is not null)
        {
            ViewModel.LastBatchResult = batchResult;
            RefreshButtonStates();
            await Info("Batch Complete", batchResult.Summary());
        }
    }

    private async void BatchPdfImages_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Batch PDF → Images")) return;
        var files = ViewModel.AllOrSelected().Select(f => f.FilePath).ToList();
        var outDir = await PickFolder(); if (outDir is null) return;
        BatchResult? batchResult = null;
        await Run($"Exporting images from {files.Count} file(s)…", async (prog, ct) =>
        {
            batchResult = await App.Services.GetRequiredService<BatchService>()
                               .BatchPdfToImagesAsync(files, outDir, prog: prog, ct: ct);
        });
        if (batchResult is not null)
        {
            ViewModel.LastBatchResult = batchResult;
            RefreshButtonStates();
            await Info("Batch Complete", batchResult.Summary());
        }
    }

    private async void ExportReport_Click(object s, RoutedEventArgs e)
    {
        if (ViewModel.LastBatchResult is null) { await Info("No Report", "Run a batch operation first."); return; }
        var out_ = await SaveFile("batch_report.csv", ".csv"); if (out_ is null) return;
        BatchService.ExportReport(ViewModel.LastBatchResult, out_);
        await Info("Report Saved", $"✓  CSV report saved to:{out_}");
    }

    // ── Files tab ─────────────────────────────────────────────────────────────

    private async void ImageEditor_Click(object s, RoutedEventArgs e)
        => await Info("Image Editor", "Image editor coming soon.");

    private async void DocxFindReplace_Click(object s, RoutedEventArgs e)
        => await Info("Find & Replace", "DOCX find/replace coming soon.");

    private async void DocxMerge_Click(object s, RoutedEventArgs e)
        => await Info("Merge DOCX", "DOCX merge coming soon.");

    private async void CsvViewer_Click(object s, RoutedEventArgs e)
        => await Info("Table Editor", "CSV/XLSX viewer coming soon.");

    // ── Activate Pro ──────────────────────────────────────────────────────────

    private async void ActivateBtn_Click(object s, RoutedEventArgs e)
    {
        var dlg = new ActivationDialog(Content.XamlRoot);
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
        {
            if (ViewModel.License.Activate(dlg.UserId, dlg.LicenseKey))
            {
                UpdateLicenseBadge();
                await Info("Pro Activated! 🎉", "All Pro features are now unlocked.");
            }
            else
            {
                await Err("Invalid Key", "That license key is not valid.Please check the key and try again.");
            }
        }
    }

    // ── Pro gate ──────────────────────────────────────────────────────────────

    private bool RequirePro(string feature)
    {
        if (ViewModel.License.IsPro)
            return true;

        _ = Info(
            "Pro Feature Required",
            $"{feature} is only available in the Pro version. Click Activate Pro in the top bar to unlock all features."
        );

        return false;
    }

    // ── Progress runner ───────────────────────────────────────────────────────

    private async Task Run(string title,
        Func<IProgress<(int, int, string)>, CancellationToken, Task> work)
    {
        var dlg = new ProgressDialog(Content.XamlRoot, title);
        _ = dlg.ShowAsync();
        try
        {
            var prog = new Progress<(int d, int t, string n)>(x => dlg.Update(x.d, x.t, x.n));
            await work(prog, dlg.CancellationToken);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await Err("Error", ex.Message); }
        finally { dlg.Hide(); }
    }

    // ── File / folder pickers ─────────────────────────────────────────────────

    private async Task<string?> SaveFile(string suggested, string ext)
    {
        try
        {
            var p = new FileSavePicker();
            var hwnd = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(p, hwnd);
            p.SuggestedFileName = suggested;
            p.FileTypeChoices.Add(ext.TrimStart('.').ToUpperInvariant(), new List<string> { ext });
            var result = await p.PickSaveFileAsync();
            return result?.Path;
        }
        catch (Exception ex)
        {
            Logger.Error($"SaveFile failed: {ex.Message}");
            throw;
        }
    }

    private async Task<string?> OpenFile(string ext)
    {
        var p = new FileOpenPicker();
        InitializeWithWindow.Initialize(p, WindowNative.GetWindowHandle(this));
        p.FileTypeFilter.Add(ext);
        return (await p.PickSingleFileAsync())?.Path;
    }

    private async Task<string?> PickFolder()
    {
        var p = new FolderPicker();
        InitializeWithWindow.Initialize(p, WindowNative.GetWindowHandle(this));
        p.FileTypeFilter.Add("*");
        return (await p.PickSingleFolderAsync())?.Path;
    }

    // ── Dialog helpers ────────────────────────────────────────────────────────

    private Task Info(string title, string msg) => new ContentDialog
    {
        Title = title,
        Content = msg,
        CloseButtonText = "OK",
        XamlRoot = Content.XamlRoot
    }.ShowAsync().AsTask();

    private Task Err(string title, string msg) => new ContentDialog
    {
        Title = title,
        Content = msg,
        CloseButtonText = "OK",
        XamlRoot = Content.XamlRoot
    }.ShowAsync().AsTask();

    private async Task<string?> Ask(string title, string prompt, string defaultValue = "")
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
            XamlRoot = Content.XamlRoot
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary ? box.Text : null;
    }

    /// <summary>Ask for a password (uses a PasswordBox so input is masked).</summary>
    private async Task<string?> AskPassword(string title, string prompt)
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
            XamlRoot = Content.XamlRoot
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary ? box.Password : null;
    }

    private async Task<int?> AskInt(string title, string prompt, int defaultValue)
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
            XamlRoot = Content.XamlRoot
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return null;
        return int.TryParse(box.Text, out int v) ? v : null;
    }

    private async Task<string?> AskQuality()
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
                    new TextBlock { Text = "prepress – maximum quality", FontSize = 12, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 8) },
                    combo
                }
            },
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary
            ? combo.SelectedItem?.ToString()
            : null;
    }

    private async Task<bool> YesNo(string title, string msg, string v)
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = msg,
            PrimaryButtonText = "Yes",
            CloseButtonText = "No",
            XamlRoot = Content.XamlRoot
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary;
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
