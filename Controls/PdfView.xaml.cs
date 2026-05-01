using PaperlessDesktop.Services;
using PaperlessDesktop.ViewModels;

namespace PaperlessDesktop.Controls;

/// <summary>Simple data item for the thumbnail strip.</summary>
internal sealed class ThumbItem
{
    public BitmapImage? Bitmap { get; init; }
    public string Label { get; init; } = "";
}

public sealed partial class PdfView : UserControl
{
    private MainViewModel _vm = null!;
    private Window _win = null!;

    private Windows.Data.Pdf.PdfDocument? _pdf;
    private uint _currentPage;
    private uint _totalPages;

    // zoom / thumbnail state
    private double _zoomFactor = 1.0;          // current render scale
    private bool _thumbStripVisible = true;
    private readonly List<ThumbItem> _thumbItems = new();

    public PdfView()
    {
        InitializeComponent();
        Loaded += (_, _) => Tab_Click(TabPages, new RoutedEventArgs());
    }

    public void Initialize(MainViewModel vm, Window win)
    {
        _vm = vm;
        _win = win;
        _vm.Files.CollectionChanged += (_, _) => RefreshButtons();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(_vm.LastBatchResult))
                BtnExportReport.IsEnabled = _vm.LastBatchResult is not null;
        };
    }

    // ── Load / Unload ─────────────────────────────────────────────────────────

    public async void LoadFile(string path)
    {
        try
        {
            FileNameBlock.Text = Path.GetFileName(path);
            _currentPage = 0;
            var file = await StorageFile.GetFileFromPathAsync(path);
            _pdf = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
            _totalPages = _pdf.PageCount;
            ActionPanelFileName.Text = Path.GetFileName(path);
            ActionPanelInfo.Text     = $"PDF · {_totalPages} page{(_totalPages == 1 ? "" : "s")}";
            PageLabel.Text = $"1 / {_totalPages}";
            PrevPageBtn.IsEnabled = false;
            NextPageBtn.IsEnabled = _totalPages > 1;
            ResetZoom();
            await RenderPageAsync(0);
            _ = LoadThumbnailsAsync();
        }
        catch { }

        RefreshButtons();
    }

    public void Clear()
    {
        _pdf = null;
        _totalPages = 0;
        _currentPage = 0;
        PageImage.Source = null;
        FileNameBlock.Text       = "";
        ActionPanelFileName.Text = "";
        ActionPanelInfo.Text     = "";
        PageLabel.Text = "0 / 0";
        PrevPageBtn.IsEnabled = false;
        NextPageBtn.IsEnabled = false;
        _thumbItems.Clear();
        ThumbRepeater.ItemsSource = null;
        ResetZoom();
        RefreshButtons();
    }

    // ── Public invoke entry points (for Features flyout in MainWindow) ─────────

    public void InvokeMerge()        => MergePdfs_Click(this, new RoutedEventArgs());
    public void InvokeSplit()        => SplitPdf_Click(this, new RoutedEventArgs());
    public void InvokeRemovePages()  => RemovePages_Click(this, new RoutedEventArgs());
    public void InvokeRotate()       => RotatePages_Click(this, new RoutedEventArgs());
    public void InvokeInsert()       => InsertPages_Click(this, new RoutedEventArgs());
    public void InvokePassword()     => PasswordProtect_Click(this, new RoutedEventArgs());
    public void InvokeUnlock()       => UnlockPdf_Click(this, new RoutedEventArgs());
    public void InvokeMetadata()     => EditMetadata_Click(this, new RoutedEventArgs());
    public void InvokeOcr()         => OcrPdf_Click(this, new RoutedEventArgs());
    public void InvokePdfToDocx()   => PdfToDocx_Click(this, new RoutedEventArgs());
    public void InvokePdfToImages() => PdfToImages_Click(this, new RoutedEventArgs());
    public void InvokeCompress()     => CompressPdf_Click(this, new RoutedEventArgs());
    public void InvokeBatchCompress()=> BatchCompress_Click(this, new RoutedEventArgs());
    public void InvokeBatchOcr()     => BatchOcr_Click(this, new RoutedEventArgs());
    public void InvokeBatchRemove()  => BatchRemove_Click(this, new RoutedEventArgs());
    public void InvokeBatchRotate()  => BatchRotate_Click(this, new RoutedEventArgs());
    public void InvokeBatchImages()  => BatchPdfImages_Click(this, new RoutedEventArgs());
    public void InvokeExportReport() => ExportReport_Click(this, new RoutedEventArgs());

    // ── Tab navigation ────────────────────────────────────────────────────────

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var tag = btn.Tag as string;

        PanePages.Visibility    = tag == "pages"    ? Visibility.Visible : Visibility.Collapsed;
        PaneSecurity.Visibility = tag == "security" ? Visibility.Visible : Visibility.Collapsed;
        PaneConvert.Visibility  = tag == "convert"  ? Visibility.Visible : Visibility.Collapsed;
        PaneOptimise.Visibility = tag == "optimise" ? Visibility.Visible : Visibility.Collapsed;
        PaneBatch.Visibility    = tag == "batch"    ? Visibility.Visible : Visibility.Collapsed;

        foreach (var tb in new[] { TabPages, TabSecurity, TabConvert, TabOptimise, TabBatch })
        {
            bool active = tb == btn;
            tb.Foreground      = active
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BrandBlueBrush"]
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextSecondaryBrush"];
            tb.BorderThickness = active ? new Thickness(0, 0, 0, 2) : new Thickness(0);
            tb.BorderBrush     = active
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BrandBlueBrush"]
                : null;
        }
    }

    // ── Page navigation

    private async void PrevPageBtn_Click(object s, RoutedEventArgs e)
    {
        if (_currentPage > 0)
        {
            _currentPage--;
            PrevPageBtn.IsEnabled = _currentPage > 0;
            NextPageBtn.IsEnabled = true;
            await RenderPageAsync(_currentPage);
        }
    }

    private async void NextPageBtn_Click(object s, RoutedEventArgs e)
    {
        if (_currentPage < _totalPages - 1)
        {
            _currentPage++;
            NextPageBtn.IsEnabled = _currentPage < _totalPages - 1;
            PrevPageBtn.IsEnabled = true;
            await RenderPageAsync(_currentPage);
        }
    }

    private async Task RenderPageAsync(uint pageIndex)
    {
        if (_pdf is null) return;
        using var page = _pdf.GetPage(pageIndex);

        // scale based on zoom; base resolution: fit to 800px wide @ 100%
        var opts = new Windows.Data.Pdf.PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(100, 800 * _zoomFactor)
        };
        var stream = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(stream, opts);
        var bmp = new BitmapImage();
        await bmp.SetSourceAsync(stream);
        PageImage.Source = bmp;
        PageImage.Width  = bmp.PixelWidth;
        PageImage.Height = bmp.PixelHeight;
        PageLabel.Text = $"{pageIndex + 1} / {_totalPages}";
        HighlightThumb(pageIndex);
    }

    // ── Zoom ──────────────────────────────────────────────────────────────────

    private void ResetZoom()
    {
        _zoomFactor = 1.0;
        ZoomSlider.Value = 100;
        ZoomLabel.Text = "100%";
    }

    private async void ApplyZoom(double factor)
    {
        _zoomFactor = Math.Clamp(factor, 0.25, 4.0);
        ZoomSlider.Value = Math.Round(_zoomFactor * 100);
        ZoomLabel.Text = $"{(int)ZoomSlider.Value}%";
        await RenderPageAsync(_currentPage);
    }

    private void ZoomSlider_ValueChanged(object s, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!IsLoaded) return;
        double factor = e.NewValue / 100.0;
        if (Math.Abs(factor - _zoomFactor) < 0.01) return;
        _zoomFactor = Math.Clamp(factor, 0.25, 4.0);
        ZoomLabel.Text = $"{(int)e.NewValue}%";
        _ = RenderPageAsync(_currentPage);
    }

    private async void ZoomFitWidth_Click(object s, RoutedEventArgs e)
    {
        if (_pdf is null) return;
        using var page = _pdf.GetPage(_currentPage);
        double viewW = PreviewScroller.ActualWidth - 32; // 16 margin each side
        double factor = viewW / page.Size.Width;
        await Task.CompletedTask; // keep async chain consistent
        ApplyZoom(factor);
    }

    private async void ZoomFitPage_Click(object s, RoutedEventArgs e)
    {
        if (_pdf is null) return;
        using var page = _pdf.GetPage(_currentPage);
        double viewW = PreviewScroller.ActualWidth - 32;
        double viewH = PreviewScroller.ActualHeight - 32;
        double fw = viewW / page.Size.Width;
        double fh = viewH / page.Size.Height;
        await Task.CompletedTask;
        ApplyZoom(Math.Min(fw, fh));
    }

    private void ZoomReset_Click(object s, RoutedEventArgs e)
    {
        ApplyZoom(1.0);
    }

    // ── Thumbnails ────────────────────────────────────────────────────────────

    private void ThumbToggle_Click(object s, RoutedEventArgs e)
    {
        _thumbStripVisible = !_thumbStripVisible;
        ThumbScroller.Visibility = _thumbStripVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task LoadThumbnailsAsync()
    {
        if (_pdf is null) return;
        _thumbItems.Clear();
        ThumbRepeater.ItemsSource = null;

        var items = new List<ThumbItem>();
        var renderOpts = new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = 52 };
        for (uint i = 0; i < _totalPages; i++)
        {
            using var page = _pdf.GetPage(i);
            var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, renderOpts);
            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(stream);
            items.Add(new ThumbItem { Bitmap = bmp, Label = $"{i + 1}" });
        }

        _thumbItems.AddRange(items);
        ThumbRepeater.ItemsSource = _thumbItems;
        HighlightThumb(0);
    }

    private void HighlightThumb(uint pageIndex)
    {
        // Walk visible ThumbBorder elements and update border color
        for (int i = 0; i < _thumbItems.Count; i++)
        {
            if (ThumbRepeater.TryGetElement(i) is Border b)
            {
                bool active = (uint)i == pageIndex;
                b.BorderBrush = active
                    ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BrandBlueBrush"]
                    : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }
    }

    private async void Thumb_Tapped(object s, TappedRoutedEventArgs e)
    {
        if (s is not Border b) return;
        int idx = ThumbRepeater.GetElementIndex(b);
        if (idx < 0 || idx >= (int)_totalPages) return;
        _currentPage = (uint)idx;
        PrevPageBtn.IsEnabled = _currentPage > 0;
        NextPageBtn.IsEnabled = _currentPage < _totalPages - 1;
        await RenderPageAsync(_currentPage);
    }

    // ── Button state ──────────────────────────────────────────────────────────

    public void RefreshButtons()
    {
        var sel = _vm.Files.Where(f => f.IsSelected).ToList();
        var selPdfs = sel.Where(f => ViewHelpers.IsPdf(f.FilePath)).ToList();
        bool hasPdfs = _vm.Files.Any(f => ViewHelpers.IsPdf(f.FilePath));
        bool oneSelPdf = selPdfs.Count == 1;
        bool manySelPdf = selPdfs.Count >= 2;

        BtnMerge.IsEnabled = manySelPdf;
        BtnRemovePages.IsEnabled = oneSelPdf;
        BtnRotate.IsEnabled = oneSelPdf;
        BtnInsert.IsEnabled = oneSelPdf;
        BtnPassword.IsEnabled = oneSelPdf;
        BtnUnlock.IsEnabled = oneSelPdf;
        BtnMetadata.IsEnabled = oneSelPdf;
        BtnOcr.IsEnabled = oneSelPdf;
        BtnToDocx.IsEnabled = oneSelPdf;
        BtnToImages.IsEnabled = oneSelPdf;
        BtnCompress.IsEnabled = oneSelPdf;
        BtnSplit.IsEnabled = oneSelPdf;
        BtnBatchCompress.IsEnabled = hasPdfs;
        BtnBatchOcr.IsEnabled = hasPdfs;
        BtnBatchRemove.IsEnabled = hasPdfs;
        BtnBatchRotate.IsEnabled = hasPdfs;
        BtnBatchToImages.IsEnabled = hasPdfs;
        BtnExportReport.IsEnabled = _vm.LastBatchResult is not null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private LicenseService License => App.Services.GetRequiredService<LicenseService>();
    private PdfCoreService Core => App.Services.GetRequiredService<PdfCoreService>();
    private PdfConvertService Convert => App.Services.GetRequiredService<PdfConvertService>();
    private BatchService Batch => App.Services.GetRequiredService<BatchService>();
    private XamlRoot Root => Content.XamlRoot;

    private bool Pro(string feature) => ViewHelpers.RequirePro(Root, License, feature);
    private FileItemViewModel? OnePdf => _vm.SelectedPdfs.FirstOrDefault();

    private Task Info(string title, string msg) => ViewHelpers.Info(Root, title, msg);
    private Task<string?> Ask(string title, string prompt, string def = "") => ViewHelpers.Ask(Root, title, prompt, def);
    private Task<string?> AskPassword(string title, string prompt) => ViewHelpers.AskPassword(Root, title, prompt);
    private Task<int?> AskInt(string title, string prompt, int def) => ViewHelpers.AskInt(Root, title, prompt, def);
    private Task<string?> AskQuality() => ViewHelpers.AskQuality(Root);
    private Task<bool> YesNo(string title, string msg, string yes, string no) => ViewHelpers.YesNo(Root, title, msg, yes, no);
    private Task<string?> Save(string suggested, string ext) => ViewHelpers.SaveFile(_win, suggested, ext);
    private Task<string?> Open(string ext) => ViewHelpers.OpenFile(_win, ext);
    private Task<string?> Folder() => ViewHelpers.PickFolder(_win);

    private Task Run(string title, Func<IProgress<(int, int, string)>, CancellationToken, Task> work)
        => ViewHelpers.Run(Root, title, work, _win as MainWindow);

    // ── PDF Actions ───────────────────────────────────────────────────────────

    private async void MergePdfs_Click(object s, RoutedEventArgs e)
    {
        var sel = _vm.SelectedPdfs.ToList();
        if (sel.Count < 2) { await Info("Merge PDFs", "Select at least 2 PDF files."); return; }
        var out_ = await Save("merged.pdf", ".pdf"); if (out_ is null) return;
        await Run("Merging PDFs…", (_, _) =>
        {
            Core.MergePdfs(sel.Select(f => f.FilePath), out_);
            return Task.CompletedTask;
        });
        await Info("Merge Complete", $"✓  {sel.Count} files merged.\n{out_}");
    }

    private async void RemovePages_Click(object s, RoutedEventArgs e)
    {
        var sel = OnePdf; if (sel is null) return;
        var spec = await Ask("Remove Pages", "Enter pages to remove:\nExamples:  3   |   1,3,5   |   2-4   |   1,3-5,7");
        if (spec is null) return;
        var out_ = await Save($"{Path.GetFileNameWithoutExtension(sel.FilePath)}_pages_removed.pdf", ".pdf");
        if (out_ is null) return;
        PdfCoreService.RemoveResult? result = null;
        await Run("Removing pages…", (_, _) =>
        {
            result = Core.RemovePages(sel.FilePath, out_, spec);
            return Task.CompletedTask;
        });
        if (result is not null)
            await Info("Done", $"✓  Removed {result.RemovedCount} page(s).\n{out_}");
    }

    private async void RotatePages_Click(object s, RoutedEventArgs e)
    {
        var sel = OnePdf; if (sel is null) return;
        var spec = await Ask("Rotate Pages", "Pages to rotate (e.g. 1,3-5 or all):", "all");
        if (spec is null) return;
        var angle = await AskInt("Rotation Angle", "Degrees (90 / 180 / 270):", 90);
        if (angle is null) return;
        if (angle.Value % 90 != 0 || angle.Value is 0 or 360)
        { await ViewHelpers.Err(Root, "Invalid Angle", "Rotation must be 90, 180, or 270 degrees."); return; }
        var out_ = await Save($"{Path.GetFileNameWithoutExtension(sel.FilePath)}_rotated.pdf", ".pdf");
        if (out_ is null) return;
        await Run("Rotating pages…", (_, _) =>
        {
            Core.RotatePages(sel.FilePath, out_, spec, angle.Value);
            return Task.CompletedTask;
        });
        await Info("Done", $"✓  Pages rotated {angle}°.\n{out_}");
    }

    private async void InsertPages_Click(object s, RoutedEventArgs e)
    {
        if (!Pro("Insert Pages")) return;
        var sel = OnePdf; if (sel is null) return;
        var src = await Open(".pdf"); if (src is null) return;
        var after = await AskInt("Insert Position", "Insert after page number (0 = beginning):", 0);
        if (after is null) return;
        var out_ = await Save($"{Path.GetFileNameWithoutExtension(sel.FilePath)}_inserted.pdf", ".pdf");
        if (out_ is null) return;
        await Run("Inserting pages…", (_, _) =>
        {
            Core.InsertPages(sel.FilePath, src, after.Value, out_);
            return Task.CompletedTask;
        });
        await Info("Done", $"✓  Pages inserted.\n{out_}");
    }

    private async void PasswordProtect_Click(object s, RoutedEventArgs e)
    {
        if (!Pro("Password Protect")) return;
        var sel = OnePdf; if (sel is null) return;
        var userPwd = await AskPassword("User Password", "Password users need to open the document:");
        if (userPwd is null) return;
        var ownerPwd = await AskPassword("Owner Password", "Owner password (controls permissions):");
        if (ownerPwd is null) return;
        var out_ = await Save($"{Path.GetFileNameWithoutExtension(sel.FilePath)}_protected.pdf", ".pdf");
        if (out_ is null) return;
        await Run("Encrypting PDF…", (_, _) =>
        {
            Core.PasswordProtect(sel.FilePath, out_, userPwd, ownerPwd);
            return Task.CompletedTask;
        });
        await Info("Done", $"✓  PDF is now password-protected.\n{out_}");
    }

    private async void UnlockPdf_Click(object s, RoutedEventArgs e)
    {
        if (!Pro("Unlock PDF")) return;
        var sel = OnePdf; if (sel is null) return;
        var pwd = await AskPassword("PDF Password", "Enter the current password to unlock:");
        if (pwd is null) return;
        var out_ = await Save($"{Path.GetFileNameWithoutExtension(sel.FilePath)}_unlocked.pdf", ".pdf");
        if (out_ is null) return;
        await Run("Unlocking PDF…", (_, _) =>
        {
            Core.UnlockPdf(sel.FilePath, out_, pwd);
            return Task.CompletedTask;
        });
        await Info("Done", $"✓  Password removed.\n{out_}");
    }

    private async void EditMetadata_Click(object s, RoutedEventArgs e)
    {
        if (!Pro("Edit Metadata")) return;
        var sel = OnePdf; if (sel is null) return;
        var title = await Ask("Title", "Document title:", ""); if (title is null) return;
        var author = await Ask("Author", "Author name:", ""); if (author is null) return;
        var subject = await Ask("Subject", "Subject:", ""); if (subject is null) return;
        var keys = await Ask("Keywords", "Keywords:", ""); if (keys is null) return;
        var out_ = await Save($"{Path.GetFileNameWithoutExtension(sel.FilePath)}_meta.pdf", ".pdf");
        if (out_ is null) return;
        await Run("Updating metadata…", (_, _) =>
        {
            Core.EditMetadata(sel.FilePath, out_, title, author, subject, keys);
            return Task.CompletedTask;
        });
        await Info("Done", $"✓  Metadata updated.\n{out_}");
    }

    private async void OcrPdf_Click(object s, RoutedEventArgs e)
    {
        if (!Pro("OCR PDF")) return;
        var sel = OnePdf; if (sel is null) return;
        var out_ = await Save($"{Path.GetFileNameWithoutExtension(sel.FilePath)}_ocr.pdf", ".pdf");
        if (out_ is null) return;
        OcrResult? ocrResult = null;
        await Run("Running OCR…", async (prog, ct) =>
        {
            ocrResult = await Convert.OcrPdfAsync(sel.FilePath, out_, progress: prog, ct: ct);
        });
        if (ocrResult is not null)
            await Info("OCR Complete", $"✓  {ocrResult.PagesProcessed} page(s) processed.\n{out_}");
    }

    private async void PdfToDocx_Click(object s, RoutedEventArgs e)
    {
        if (!Pro("PDF → Word")) return;
        var sel = OnePdf; if (sel is null) return;
        var out_ = await Save($"{Path.GetFileNameWithoutExtension(sel.FilePath)}.docx", ".docx");
        if (out_ is null) return;
        await Run("Converting to Word…", async (_, ct) =>
        {
            await Convert.PdfToDocxAsync(sel.FilePath, out_, ct: ct);
        });
        await Info("Conversion Complete", $"✓  Saved as Word document.\n{out_}");
    }

    private async void PdfToImages_Click(object s, RoutedEventArgs e)
    {
        if (!Pro("PDF → Images")) return;
        var sel = OnePdf; if (sel is null) return;
        var outDir = await Folder(); if (outDir is null) return;
        List<string>? exported = null;
        await Run("Exporting pages as images…", async (prog, ct) =>
        {
            exported = await Convert.PdfToImagesAsync(sel.FilePath, outDir, progress: prog, ct: ct);
        });
        if (exported is not null)
            await Info("Export Complete", $"✓  {exported.Count} image(s) saved to:\n{outDir}");
    }

    private async void CompressPdf_Click(object s, RoutedEventArgs e)
    {
        if (!Pro("Compress PDF")) return;
        var sel = OnePdf; if (sel is null) return;
        var quality = await AskQuality(); if (quality is null) return;
        var out_ = await Save($"{Path.GetFileNameWithoutExtension(sel.FilePath)}_compressed.pdf", ".pdf");
        if (out_ is null) return;
        PdfCoreService.CompressResult? compressResult = null;
        await Run("Compressing PDF…", (_, _) =>
        {
            compressResult = Core.CompressPdf(sel.FilePath, out_, quality);
            return Task.CompletedTask;
        });
        if (compressResult is not null)
        {
            var msg = compressResult.KeptOriginal
                ? $"File was already optimal — original quality preserved.\nSize: {ViewHelpers.FormatBytes(compressResult.OutputSize)}"
                : $"✓  {ViewHelpers.FormatBytes(compressResult.OriginalSize)}  →  {ViewHelpers.FormatBytes(compressResult.OutputSize)}  ({compressResult.SavingsPct:F1}% smaller)";
            await Info("Compression Complete", msg + $"\n{out_}");
        }
    }

    private async void SplitPdf_Click(object s, RoutedEventArgs e)
    {
        if (!Pro("Split PDF")) return;
        var sel = OnePdf; if (sel is null) return;
        var outputDir = await Folder(); if (outputDir is null) return;
        bool splitByN = await YesNo("Split Method", "How would you like to split this PDF?",
            "Every N pages", "Custom ranges");
        PdfCoreService.SplitResult result;
        if (splitByN)
        {
            var n = await AskInt("Pages per chunk", "Split every N pages:", 2);
            if (n is null) return;
            result = Core.SplitEveryN(sel.FilePath, outputDir, n.Value);
        }
        else
        {
            var ranges = await Ask("Page Ranges", "Enter ranges (e.g. 1-3, 5, 7-9):");
            if (ranges is null) return;
            result = Core.SplitByRanges(sel.FilePath, outputDir, ranges);
        }
        await Info("Split Complete", $"✓  Created {result.OutputFiles.Count} file(s) in:\n{outputDir}");
    }

    // ── Batch Actions ─────────────────────────────────────────────────────────

    private async void BatchCompress_Click(object s, RoutedEventArgs e)
    {
        if (!Pro("Batch Compress")) return;
        var quality = await AskQuality(); if (quality is null) return;
        var files = _vm.AllOrSelected().Select(f => f.FilePath).ToList();
        var outDir = await Folder(); if (outDir is null) return;
        BatchResult? batchResult = null;
        await Run($"Compressing {files.Count} file(s)…", async (prog, ct) =>
        {
            batchResult = await Batch.BatchCompressAsync(files, outDir, quality, prog, ct);
        });
        if (batchResult is not null)
        {
            _vm.LastBatchResult = batchResult;
            BtnExportReport.IsEnabled = true;
            await Info("Batch Complete", batchResult.Summary());
        }
    }

    private async void BatchOcr_Click(object s, RoutedEventArgs e)
    {
        if (!Pro("Batch OCR")) return;
        var files = _vm.AllOrSelected().Select(f => f.FilePath).ToList();
        var outDir = await Folder(); if (outDir is null) return;
        BatchResult? batchResult = null;
        await Run($"OCR processing {files.Count} file(s)…", async (prog, ct) =>
        {
            batchResult = await Batch.BatchOcrAsync(files, outDir, prog: prog, ct: ct);
        });
        if (batchResult is not null)
        {
            _vm.LastBatchResult = batchResult;
            BtnExportReport.IsEnabled = true;
            await Info("Batch Complete", batchResult.Summary());
        }
    }

    private async void BatchRemove_Click(object s, RoutedEventArgs e)
    {
        if (!Pro("Batch Remove Pages")) return;
        var spec = await Ask("Remove Pages", "Pages to remove from ALL files (e.g. 1,3-5):"); if (spec is null) return;
        var files = _vm.AllOrSelected().Select(f => f.FilePath).ToList();
        var outDir = await Folder(); if (outDir is null) return;
        BatchResult? batchResult = null;
        await Run($"Removing pages from {files.Count} file(s)…", async (prog, ct) =>
        {
            batchResult = await Batch.BatchRemovePagesAsync(files, outDir, spec, prog, ct);
        });
        if (batchResult is not null)
        {
            _vm.LastBatchResult = batchResult;
            BtnExportReport.IsEnabled = true;
            await Info("Batch Complete", batchResult.Summary());
        }
    }

    private async void BatchRotate_Click(object s, RoutedEventArgs e)
    {
        if (!Pro("Batch Rotate")) return;
        var spec = await Ask("Pages", "Pages to rotate (all or e.g. 1,3-5):", "all"); if (spec is null) return;
        var angle = await AskInt("Angle", "Rotation angle (90/180/270):", 90); if (angle is null) return;
        var files = _vm.AllOrSelected().Select(f => f.FilePath).ToList();
        var outDir = await Folder(); if (outDir is null) return;
        BatchResult? batchResult = null;
        await Run($"Rotating {files.Count} file(s)…", async (prog, ct) =>
        {
            batchResult = await Batch.BatchRotateAsync(files, outDir, spec, angle.Value, prog, ct);
        });
        if (batchResult is not null)
        {
            _vm.LastBatchResult = batchResult;
            BtnExportReport.IsEnabled = true;
            await Info("Batch Complete", batchResult.Summary());
        }
    }

    private async void BatchPdfImages_Click(object s, RoutedEventArgs e)
    {
        if (!Pro("Batch PDF → Images")) return;
        var files = _vm.AllOrSelected().Select(f => f.FilePath).ToList();
        var outDir = await Folder(); if (outDir is null) return;
        BatchResult? batchResult = null;
        await Run($"Exporting images from {files.Count} file(s)…", async (prog, ct) =>
        {
            batchResult = await Batch.BatchPdfToImagesAsync(files, outDir, prog: prog, ct: ct);
        });
        if (batchResult is not null)
        {
            _vm.LastBatchResult = batchResult;
            BtnExportReport.IsEnabled = true;
            await Info("Batch Complete", batchResult.Summary());
        }
    }

    private async void ExportReport_Click(object s, RoutedEventArgs e)
    {
        if (_vm.LastBatchResult is null) { await Info("No Report", "Run a batch operation first."); return; }
        var out_ = await Save("batch_report.csv", ".csv"); if (out_ is null) return;
        BatchService.ExportReport(_vm.LastBatchResult, out_);
        await Info("Report Saved", $"✓  CSV report saved to:\n{out_}");
    }
}
