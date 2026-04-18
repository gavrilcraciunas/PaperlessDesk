using PaperlessDesktop.Dialogs;
using PaperlessDesktop.Services;
using PaperlessDesktop.ViewModels;
using PaperlessDesktop.Interop;
using PaperlessDesktop.Shared;
using System.Runtime.InteropServices;

namespace PaperlessDesktop;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        ExtendsContentIntoTitleBar = true;
        LicenseStatusBlock.Text = ViewModel.License.LicenseStatusText;
        FileListView.ItemsSource = ViewModel.Files;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.StatusText))
                StatusBlock.Text = ViewModel.StatusText;
            if (e.PropertyName == nameof(ViewModel.FileCountText))
                FileCountBlock.Text = ViewModel.FileCountText;
        };
    }

    private async void AddBtn_Click(object s, RoutedEventArgs e)
    {
        try
        {
            // First, get and validate the window handle
            IntPtr hwnd = IntPtr.Zero;
            try
            {
                hwnd = WindowNative.GetWindowHandle(this);
                Logger.Info($"Window handle: {hwnd}");

                if (hwnd == IntPtr.Zero)
                {
                    await Err("Error", "Could not obtain window handle");
                    return;
                }
            }
            catch (Exception hwndEx)
            {
                Logger.Error($"Failed to get window handle: {hwndEx.Message}");
                await Err("Window Error", $"Failed to get window handle: {hwndEx.Message}");
                return;
            }

            var picker = new FileOpenPicker();

            // Try to initialize picker with window
            try
            {
                InitializeWithWindow.Initialize(picker, hwnd);
                Logger.Info("Picker initialized successfully");
            }
            catch (Exception initEx)
            {
                Logger.Warn($"Picker initialization failed (continuing anyway): {initEx.Message}");
            }

            // Add file types
            foreach (var ext in new[] { ".pdf", ".png", ".jpg", ".jpeg", ".docx", ".csv", ".xlsx" })
                picker.FileTypeFilter.Add(ext);
            picker.ViewMode = PickerViewMode.List;

            // Now try to pick files
            Logger.Info("Opening file picker...");
            var files = await picker.PickMultipleFilesAsync();

            if (files?.Count > 0) 
            {
                Logger.Info($"Selected {files.Count} file(s), adding to ViewModel");
                ViewModel.AddFiles(files.Select(f => f.Path));
                Logger.Info($"Successfully added {files.Count} file(s)");
            }
            else
            {
                Logger.Info("No files selected");
            }
        }
        catch (COMException comEx) when (comEx.HResult == unchecked((int)0x80070578))
        {
            Logger.Error($"Invalid window handle COM error: {comEx.Message}");
            await Err("Window Handle Error", "The file picker encountered an issue with the window handle. Try restarting the application.");
        }
        catch (Exception ex)
        {
            Logger.Error($"AddBtn_Click error: {ex.Message}", ex);
            await Err("Error", $"Failed to open file picker: {ex.Message}");
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
        if (await dlg.ShowAsync() == ContentDialogResult.Primary) ViewModel.ClearFiles();
    }

    private void MoveUpBtn_Click(object s, RoutedEventArgs e)
    { if (FileListView.SelectedItem is FileItemViewModel item) ViewModel.MoveUp(item); }

    private void MoveDownBtn_Click(object s, RoutedEventArgs e)
    { if (FileListView.SelectedItem is FileItemViewModel item) ViewModel.MoveDown(item); }

    private void FileListView_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        var sel = FileListView.SelectedItems.Cast<FileItemViewModel>().ToList();
        if (sel.Count == 1 && sel[0].FilePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            Preview.LoadPdf(sel[0].FilePath);
    }

    private void FileListView_DragOver(object s, DragEventArgs e)
        => e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;

    private async void FileListView_Drop(object s, DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            ViewModel.AddFiles(items.Select(i => i.Path));
        }
    }

    private async void MergePdfs_Click(object s, RoutedEventArgs e)
    {
        var sel = ViewModel.SelectedPdfs.ToList();
        if (sel.Count < 2) { await Info("Merge", "Select at least 2 PDFs."); return; }
        var out_ = await SaveFile("merged.pdf", ".pdf"); if (out_ is null) return;
        await Run("Merging...", async (_, _) =>
        {
            App.Services.GetRequiredService<PdfCoreService>().MergePdfs(sel.Select(f => f.FilePath), out_);
            await Info("Done", $"Saved:\n{out_}");
        });
    }

    private async void RemovePages_Click(object s, RoutedEventArgs e)
    {
        var sel = ViewModel.SelectedPdfs.FirstOrDefault();
        if (sel is null) { await Info("Remove Pages", "Select one PDF."); return; }
        var spec = await Ask("Remove Pages", "Pages to remove (e.g. 1,3-5,7):"); if (spec is null) return;
        var out_ = await SaveFile("pages_removed.pdf", ".pdf"); if (out_ is null) return;
        await Run("Removing pages...", async (_, _) =>
        {
            App.Services.GetRequiredService<PdfCoreService>().RemovePages(sel.FilePath, out_, spec);
            await Info("Done", $"Saved:\n{out_}");
        });
    }

    private async void RotatePages_Click(object s, RoutedEventArgs e)
    {
        var sel = ViewModel.SelectedPdfs.FirstOrDefault();
        if (sel is null) { await Info("Rotate", "Select one PDF."); return; }
        var spec = await Ask("Rotate Pages", "Pages (e.g. 1,3-5 or all):", "all"); if (spec is null) return;
        var angle = await AskInt("Angle", "Rotation angle (90/180/270):", 90); if (angle is null) return;
        var out_ = await SaveFile("rotated.pdf", ".pdf"); if (out_ is null) return;
        await Run("Rotating...", async (_, _) =>
        {
            App.Services.GetRequiredService<PdfCoreService>().RotatePages(sel.FilePath, out_, spec, angle.Value);
            await Info("Done", $"Saved:\n{out_}");
        });
    }

    private async void InsertPages_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Insert Pages")) return;
        var sel = ViewModel.SelectedPdfs.FirstOrDefault();
        if (sel is null) { await Info("Insert Pages", "Select the base PDF."); return; }
        var src = await OpenFile(".pdf"); if (src is null) return;
        var after = await AskInt("Insert after page", "After page number (0 = beginning):", 0); if (after is null) return;
        var out_ = await SaveFile("with_insert.pdf", ".pdf"); if (out_ is null) return;
        App.Services.GetRequiredService<PdfCoreService>().InsertPages(sel.FilePath, src, after.Value, out_);
        await Info("Done", $"Saved:\n{out_}");
    }

    private async void PasswordProtect_Click(object s, RoutedEventArgs e)
    { if (!RequirePro("Password Protect")) return; await Info("Coming soon", "Requires iText7 — see roadmap Step 6."); }

    private async void UnlockPdf_Click(object s, RoutedEventArgs e)
    { if (!RequirePro("Unlock PDF")) return; await Info("Coming soon", "Requires iText7 — see roadmap Step 6."); }

    private async void EditMetadata_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Edit Metadata")) return;
        var sel = ViewModel.SelectedPdfs.FirstOrDefault();
        if (sel is null) { await Info("Edit Metadata", "Select one PDF."); return; }
        var title = await Ask("Title", "Document title:", ""); if (title is null) return;
        var author = await Ask("Author", "Author:", ""); if (author is null) return;
        var subject = await Ask("Subject", "Subject:", ""); if (subject is null) return;
        var keys = await Ask("Keywords", "Keywords:", ""); if (keys is null) return;
        var out_ = await SaveFile("metadata_edited.pdf", ".pdf"); if (out_ is null) return;
        App.Services.GetRequiredService<PdfCoreService>().EditMetadata(sel.FilePath, out_, title, author, subject, keys);
        await Info("Done", $"Saved:\n{out_}");
    }

    private async void OcrPdf_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("OCR PDF")) return;
        var sel = ViewModel.SelectedPdfs.FirstOrDefault();
        if (sel is null) { await Info("OCR", "Select one PDF."); return; }
        var out_ = await SaveFile(Path.GetFileNameWithoutExtension(sel.FilePath) + "_ocr.pdf", ".pdf"); if (out_ is null) return;
        await Run("Running OCR...", async (prog, ct) =>
        {
            var res = await App.Services.GetRequiredService<PdfConvertService>()
                               .OcrPdfAsync(sel.FilePath, out_, progress: prog, ct: ct);
            await Info("OCR complete", $"Processed {res.PagesProcessed} pages.\nSaved:\n{out_}");
        });
    }

    private async void PdfToDocx_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("PDF to DOCX")) return;
        var sel = ViewModel.SelectedPdfs.FirstOrDefault();
        if (sel is null) { await Info("PDF to DOCX", "Select one PDF."); return; }
        var out_ = await SaveFile(Path.GetFileNameWithoutExtension(sel.FilePath) + ".docx", ".docx"); if (out_ is null) return;
        await Run("Converting...", async (_, ct) =>
        {
            await App.Services.GetRequiredService<PdfConvertService>().PdfToDocxAsync(sel.FilePath, out_, ct: ct);
            await Info("Done", $"Saved:\n{out_}");
        });
    }

    private async void PdfToImages_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("PDF to Images")) return;
        var sel = ViewModel.SelectedPdfs.FirstOrDefault();
        if (sel is null) { await Info("PDF to Images", "Select one PDF."); return; }
        var outDir = await PickFolder(); if (outDir is null) return;
        await Run("Exporting images...", async (prog, ct) =>
        {
            var imgs = await App.Services.GetRequiredService<PdfConvertService>()
                                .PdfToImagesAsync(sel.FilePath, outDir, progress: prog, ct: ct);
            await Info("Done", $"Exported {imgs.Count} images to:\n{outDir}");
        });
    }

    private async void ImagesToPdf_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Images to PDF")) return;
        var images = ViewModel.SelectedImages.ToList();
        if (images.Count == 0) { await Info("Images to PDF", "Select at least one image."); return; }
        var out_ = await SaveFile("images.pdf", ".pdf"); if (out_ is null) return;
        await Run("Creating PDF...", async (prog, ct) =>
        {
            await App.Services.GetRequiredService<PdfConvertService>()
                               .ImagesToPdfAsync(images.Select(f => f.FilePath), out_, progress: prog, ct: ct);
            await Info("Done", $"Saved:\n{out_}");
        });
    }

    private async void CompressPdf_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Compress PDF")) return;
        var sel = ViewModel.SelectedPdfs.FirstOrDefault();
        if (sel is null) { await Info("Compress PDF", "Select one PDF."); return; }
        var quality = await AskQuality(); if (quality is null) return;
        var out_ = await SaveFile(Path.GetFileNameWithoutExtension(sel.FilePath) + "_compressed.pdf", ".pdf"); if (out_ is null) return;
        await Run("Compressing...", async (_, _) =>
        {
            var res = App.Services.GetRequiredService<PdfCoreService>().CompressPdf(sel.FilePath, out_, quality);
            var msg = res.KeptOriginal
                ? $"Already optimal — original kept.\nSize: {FormatBytes(res.OutputSize)}"
                : $"{FormatBytes(res.OriginalSize)} → {FormatBytes(res.OutputSize)} ({res.SavingsPct:F1}% saved)";
            await Info("Compress PDF", msg + $"\n\nSaved:\n{out_}");
        });
    }

    private async void SplitPdf_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Split PDF")) return;
        var sel = ViewModel.SelectedPdfs.FirstOrDefault();
        if (sel is null) { await Info("Split PDF", "Select one PDF."); return; }
        var outDir = await PickFolder(); if (outDir is null) return;
        var byN = await YesNo("Split method", "Yes = every N pages\nNo = custom ranges");
        PdfCoreService.SplitResult res;
        if (byN)
        {
            var n = await AskInt("Every N pages", "Enter N:", 2); if (n is null) return;
            res = App.Services.GetRequiredService<PdfCoreService>().SplitEveryN(sel.FilePath, outDir, n.Value);
        }
        else
        {
            var spec = await Ask("Ranges", "Ranges (e.g. 1-3,5,7-9):"); if (spec is null) return;
            res = App.Services.GetRequiredService<PdfCoreService>().SplitByRanges(sel.FilePath, outDir, spec);
        }
        await Info("Done", $"Created {res.OutputFiles.Count} file(s) in:\n{outDir}");
    }

    private async void ImageEditor_Click(object s, RoutedEventArgs e) => await Info("Image Editor", "See roadmap Step 3.");
    private async void DocxFindReplace_Click(object s, RoutedEventArgs e) => await Info("Find & Replace", "See roadmap Step 4.");
    private async void DocxMerge_Click(object s, RoutedEventArgs e) => await Info("Merge DOCX", "See roadmap Step 4.");
    private async void CsvViewer_Click(object s, RoutedEventArgs e) => await Info("Table Editor", "See roadmap Step 5.");

    private async void BatchCompress_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Batch Compress")) return;
        var quality = await AskQuality(); if (quality is null) return;
        var files = ViewModel.AllOrSelected().Select(f => f.FilePath).ToList();
        var outDir = await PickFolder(); if (outDir is null) return;
        await Run("Batch Compress...", async (prog, ct) =>
        {
            var res = await App.Services.GetRequiredService<BatchService>()
                               .BatchCompressAsync(files, outDir, quality, prog, ct);
            await Info("Done", res.Summary());
        });
    }

    private async void BatchOcr_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Batch OCR")) return;
        var files = ViewModel.AllOrSelected().Select(f => f.FilePath).ToList();
        var outDir = await PickFolder(); if (outDir is null) return;
        await Run("Batch OCR...", async (prog, ct) =>
        {
            var res = await App.Services.GetRequiredService<BatchService>()
                               .BatchOcrAsync(files, outDir, prog: prog, ct: ct);
            await Info("Done", res.Summary());
        });
    }

    private async void BatchRemove_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Batch Remove Pages")) return;
        var spec = await Ask("Pages", "Pages to remove (e.g. 1,3-5):"); if (spec is null) return;
        var files = ViewModel.AllOrSelected().Select(f => f.FilePath).ToList();
        var outDir = await PickFolder(); if (outDir is null) return;
        await Run("Batch Remove Pages...", async (prog, ct) =>
        {
            var res = await App.Services.GetRequiredService<BatchService>()
                               .BatchRemovePagesAsync(files, outDir, spec, prog, ct);
            await Info("Done", res.Summary());
        });
    }

    private async void BatchRotate_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Batch Rotate")) return;
        var spec = await Ask("Pages", "Pages (all or e.g. 1,3-5):", "all"); if (spec is null) return;
        var angle = await AskInt("Angle", "Rotation angle (90/180/270):", 90); if (angle is null) return;
        var files = ViewModel.AllOrSelected().Select(f => f.FilePath).ToList();
        var outDir = await PickFolder(); if (outDir is null) return;
        await Run("Batch Rotate...", async (prog, ct) =>
        {
            var res = await App.Services.GetRequiredService<BatchService>()
                               .BatchRotateAsync(files, outDir, spec, angle.Value, prog, ct);
            await Info("Done", res.Summary());
        });
    }

    private async void BatchPdfImages_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Batch PDF to Images")) return;
        var files = ViewModel.AllOrSelected().Select(f => f.FilePath).ToList();
        var outDir = await PickFolder(); if (outDir is null) return;
        await Run("Batch PDF to Images...", async (prog, ct) =>
        {
            var res = await App.Services.GetRequiredService<BatchService>()
                               .BatchPdfToImagesAsync(files, outDir, prog: prog, ct: ct);
            await Info("Done", res.Summary());
        });
    }

    private async void ExportReport_Click(object s, RoutedEventArgs e)
    {
        if (ViewModel.LastBatchResult is null) { await Info("Export Report", "Run a batch operation first."); return; }
        var out_ = await SaveFile("batch_report.csv", ".csv"); if (out_ is null) return;
        BatchService.ExportReport(ViewModel.LastBatchResult, out_);
        await Info("Done", $"Report saved:\n{out_}");
    }

    private async void ActivateBtn_Click(object s, RoutedEventArgs e)
    {
        var dlg = new ActivationDialog(Content.XamlRoot);
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
        {
            if (ViewModel.License.Activate(dlg.UserId, dlg.LicenseKey))
            {
                LicenseStatusBlock.Text = ViewModel.License.LicenseStatusText;
                await Info("Activated", "Pro unlocked successfully!");
            }
            else await Err("Invalid key", "That key is not valid.");
        }
    }

    private bool RequirePro(string feature)
    {
        if (ViewModel.License.IsPro) return true;
        _ = Info("Pro feature", $"'{feature}' requires Pro. Click Activate Pro to unlock.");
        return false;
    }

    private async Task Run(string title,
        Func<IProgress<(int, int, string)>, CancellationToken, Task> work)
    {
        var dlg = new ProgressDialog(Content.XamlRoot, title); _ = dlg.ShowAsync();
        try
        {
            var prog = new Progress<(int d, int t, string n)>(x => dlg.Update(x.d, x.t, x.n));
            await work(prog, dlg.CancellationToken);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await Err("Error", ex.Message); }
        finally { dlg.Hide(); }
    }

    private async Task<string?> SaveFile(string name, string ext)
    {
        try
        {
            var p = new FileSavePicker();
            var hwnd = WindowNative.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero) return null;

            InitializeWithWindow.Initialize(p, hwnd);
            p.SuggestedFileName = name;
            p.FileTypeChoices.Add(ext.TrimStart('.').ToUpperInvariant(), new List<string> { ext });
            return (await p.PickSaveFileAsync())?.Path;
        }
        catch (Exception ex)
        {
            Logger.Error($"SaveFile error: {ex.Message}", ex);
            await Err("Save Error", $"Failed to save file: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> OpenFile(string ext)
    {
        try
        {
            var p = new FileOpenPicker();
            var hwnd = WindowNative.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero) return null;

            InitializeWithWindow.Initialize(p, hwnd);
            p.FileTypeFilter.Add(ext);
            return (await p.PickSingleFileAsync())?.Path;
        }
        catch (Exception ex)
        {
            Logger.Error($"OpenFile error: {ex.Message}", ex);
            await Err("Open Error", $"Failed to open file: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> PickFolder()
    {
        try
        {
            var p = new FolderPicker();
            var hwnd = WindowNative.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero) return null;

            InitializeWithWindow.Initialize(p, hwnd);
            p.FileTypeFilter.Add("*");
            return (await p.PickSingleFolderAsync())?.Path;
        }
        catch (Exception ex)
        {
            Logger.Error($"PickFolder error: {ex.Message}", ex);
            await Err("Folder Error", $"Failed to pick folder: {ex.Message}");
            return null;
        }
    }

    private Task Info(string title, string msg) => new ContentDialog
    { Title = title, Content = msg, CloseButtonText = "OK", XamlRoot = Content.XamlRoot }.ShowAsync().AsTask();

    private Task Err(string title, string msg) => new ContentDialog
    { Title = title, Content = msg, CloseButtonText = "OK", XamlRoot = Content.XamlRoot }.ShowAsync().AsTask();

    private async Task<string?> Ask(string title, string prompt, string def = "")
    {
        var tb = new TextBox { Text = def };
        var dlg = new ContentDialog
        {
            Title = title,
            Content = new StackPanel { Children = { new TextBlock { Text = prompt }, tb } },
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary ? tb.Text : null;
    }

    private async Task<int?> AskInt(string title, string prompt, int def)
    {
        var nb = new NumberBox { Value = def, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var dlg = new ContentDialog
        {
            Title = title,
            Content = new StackPanel { Children = { new TextBlock { Text = prompt }, nb } },
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary ? (int)nb.Value : null;
    }

    private async Task<string?> AskQuality()
    {
        var cb = new ComboBox
        {
            ItemsSource = new[] { "screen", "ebook", "printer", "prepress" },
            SelectedItem = "ebook",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var dlg = new ContentDialog
        {
            Title = "Compression quality",
            Content = new StackPanel
            {
                Children = {
                new TextBlock { Text = "screen=smallest  ebook=balanced  printer/prepress=quality",
                                Opacity = 0.6, TextWrapping = TextWrapping.Wrap }, cb }
            },
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary ? cb.SelectedItem as string : null;
    }

    private async Task<bool> YesNo(string title, string prompt)
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = prompt,
            PrimaryButtonText = "Yes",
            SecondaryButtonText = "No",
            XamlRoot = Content.XamlRoot
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary;
    }

    private static string FormatBytes(long n)
    {
        foreach (var u in new[] { "B", "KB", "MB", "GB" })
        {
            if (n < 1024) return $"{n:F1} {u}";
            n /= 1024;
        }
        return $"{n:F1} TB";
    }
}
