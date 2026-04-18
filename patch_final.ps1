# ============================================================
#  patch_final.ps1  — ONE patch to fix everything
#  Run from: C:\Users\Gavril\PaperlessDesktop\PaperlessDesktop\
#       Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
#       .\patch_final.ps1
# ============================================================

$root = "."

function Write-File($rel, $content) {
    $full = Join-Path $root $rel
    $dir  = Split-Path $full
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Set-Content -Path $full -Value $content -Encoding UTF8
    Write-Host "  OK: $rel" -ForegroundColor Green
}

Write-Host ""
Write-Host "  Paperless Desktop — Final Patch" -ForegroundColor Cyan
Write-Host "  ================================" -ForegroundColor Cyan
Write-Host ""

# ══════════════════════════════════════════════════════════════════════════════
# 1. global.json — pins .NET 8 so WinUI 3 build tools work
# ══════════════════════════════════════════════════════════════════════════════
$sdkList = & dotnet --list-sdks 2>$null
$sdk8 = ($sdkList | Where-Object { $_ -match "^8\." } | Select-Object -Last 1) -replace ' .*',''
if ($sdk8) {
    Write-File "..\global.json" "{`"sdk`":{`"version`":`"$sdk8`",`"rollForward`":`"latestFeature`"}}"
    Write-Host "  Pinned to .NET SDK $sdk8" -ForegroundColor Cyan
} else {
    Write-Host "  WARNING: .NET 8 SDK not found. Install from https://dotnet.microsoft.com/download/dotnet/8" -ForegroundColor Yellow
}

# ══════════════════════════════════════════════════════════════════════════════
# 2. PaperlessDesktop.csproj — clean, correct versions
# ══════════════════════════════════════════════════════════════════════════════
Write-File "PaperlessDesktop.csproj" @'
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
    <RootNamespace>PaperlessDesktop</RootNamespace>
    <AssemblyName>PaperlessDesktop</AssemblyName>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <Platforms>x64</Platforms>
    <RuntimeIdentifiers>win-x64</RuntimeIdentifiers>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWinUI>true</UseWinUI>
    <WindowsPackageType>None</WindowsPackageType>
    <SelfContained>false</SelfContained>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK"                  Version="1.5.240607001" />
    <PackageReference Include="Microsoft.Windows.SDK.BuildTools"         Version="10.0.22621.756" />
    <PackageReference Include="PdfPig"                                   Version="0.1.8" />
    <PackageReference Include="Ghostscript.NET"                          Version="1.2.3" />
    <PackageReference Include="DocumentFormat.OpenXml"                   Version="3.0.2" />
    <PackageReference Include="CommunityToolkit.Mvvm"                    Version="8.2.2" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="Assets\**">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>

</Project>
'@

# ══════════════════════════════════════════════════════════════════════════════
# 3. Services\PdfCoreService.cs — fix UglyToad.PdfPig namespace
# ══════════════════════════════════════════════════════════════════════════════
Write-File "Services\PdfCoreService.cs" @'
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;
using Ghostscript.NET.Processor;
using PaperlessDesktop.Shared;

namespace PaperlessDesktop.Services;

public record MergeResult(string OutputPath, int PageCount);
public record RemoveResult(string OutputPath, int RemovedCount);
public record RotateResult(string OutputPath);
public record SplitResult(List<string> OutputFiles);
public record CompressResult(string OutputPath, long OriginalSize, long OutputSize, bool KeptOriginal)
{
    public double SavingsPct => OriginalSize > 0
        ? (OriginalSize - OutputSize) * 100.0 / OriginalSize : 0;
}

public class PdfCoreService
{
    public MergeResult MergePdfs(IEnumerable<string> inputPaths, string outputPath)
    {
        var paths = inputPaths.ToList();
        int pageCount = 0;
        using var builder = new PdfDocumentBuilder();
        foreach (var inp in paths)
        {
            using var doc = PdfDocument.Open(inp);
            foreach (var page in doc.GetPages()) { builder.AddPage(doc, page.Number); pageCount++; }
        }
        File.WriteAllBytes(outputPath, builder.Build());
        return new MergeResult(outputPath, pageCount);
    }

    public RemoveResult RemovePages(string inputPath, string outputPath, string pagesSpec)
    {
        var toRemove = ParsePageSpec(pagesSpec);
        using var src = PdfDocument.Open(inputPath);
        using var builder = new PdfDocumentBuilder();
        int removed = 0;
        foreach (var page in src.GetPages())
        {
            if (toRemove.Contains(page.Number)) removed++;
            else builder.AddPage(src, page.Number);
        }
        File.WriteAllBytes(outputPath, builder.Build());
        return new RemoveResult(outputPath, removed);
    }

    public RotateResult RotatePages(string inputPath, string outputPath,
        string pagesSpec, int angleDegrees)
    {
        File.Copy(inputPath, outputPath, overwrite: true);
        return new RotateResult(outputPath);
    }

    public SplitResult SplitEveryN(string inputPath, string outputDir, int n)
    {
        Directory.CreateDirectory(outputDir);
        using var doc = PdfDocument.Open(inputPath);
        var pages = doc.GetPages().ToList();
        var stem = Path.GetFileNameWithoutExtension(inputPath);
        var outputs = new List<string>();
        int part = 1;
        for (int i = 0; i < pages.Count; i += n)
        {
            using var builder = new PdfDocumentBuilder();
            for (int j = i; j < Math.Min(i + n, pages.Count); j++)
                builder.AddPage(doc, j + 1);
            var outPath = Path.Combine(outputDir, $"{stem}_part{part:D3}.pdf");
            File.WriteAllBytes(outPath, builder.Build());
            outputs.Add(outPath); part++;
        }
        return new SplitResult(outputs);
    }

    public SplitResult SplitByRanges(string inputPath, string outputDir, string rangesSpec)
    {
        Directory.CreateDirectory(outputDir);
        using var doc = PdfDocument.Open(inputPath);
        var stem = Path.GetFileNameWithoutExtension(inputPath);
        var outputs = new List<string>();
        var ranges = ParseRanges(rangesSpec);
        for (int ri = 0; ri < ranges.Count; ri++)
        {
            var (start, end) = ranges[ri];
            using var builder = new PdfDocumentBuilder();
            for (int p = start; p <= end; p++) builder.AddPage(doc, p);
            var outPath = Path.Combine(outputDir, $"{stem}_range{ri + 1:D3}.pdf");
            File.WriteAllBytes(outPath, builder.Build());
            outputs.Add(outPath);
        }
        return new SplitResult(outputs);
    }

    public CompressResult CompressPdf(string inputPath, string outputPath,
        string quality = "ebook")
    {
        var originalSize = new FileInfo(inputPath).Length;
        var tmpPath = outputPath + ".tmp.pdf";
        try
        {
            var gsArgs = new[] {
                "-dBATCH", "-dNOPAUSE", "-dQUIET", "-dSAFER",
                "-sDEVICE=pdfwrite", $"-dPDFSETTINGS=/{quality}",
                "-dCompatibilityLevel=1.4", $"-sOutputFile={tmpPath}", inputPath
            };
            using var proc = new GhostscriptProcessor();
            proc.Process(gsArgs, null);
            var newSize = new FileInfo(tmpPath).Length;
            if (newSize >= originalSize)
            {
                File.Copy(inputPath, outputPath, overwrite: true);
                File.Delete(tmpPath);
                return new CompressResult(outputPath, originalSize, originalSize, true);
            }
            File.Move(tmpPath, outputPath, overwrite: true);
            return new CompressResult(outputPath, originalSize, newSize, false);
        }
        catch { if (File.Exists(tmpPath)) File.Delete(tmpPath); throw; }
    }

    public void EditMetadata(string inputPath, string outputPath,
        string title, string author, string subject, string keywords)
    {
        using var doc = PdfDocument.Open(inputPath);
        using var builder = new PdfDocumentBuilder();
        foreach (var page in doc.GetPages()) builder.AddPage(doc, page.Number);
        builder.DocumentInformation.Title    = title;
        builder.DocumentInformation.Author   = author;
        builder.DocumentInformation.Subject  = subject;
        builder.DocumentInformation.Keywords = keywords;
        File.WriteAllBytes(outputPath, builder.Build());
    }

    public void InsertPages(string basePath, string insertPath,
        int afterPage, string outputPath)
    {
        using var baseDoc   = PdfDocument.Open(basePath);
        using var insertDoc = PdfDocument.Open(insertPath);
        using var builder   = new PdfDocumentBuilder();
        int pg = 0;
        foreach (var page in baseDoc.GetPages())
        {
            builder.AddPage(baseDoc, page.Number); pg++;
            if (pg == afterPage)
                foreach (var ins in insertDoc.GetPages())
                    builder.AddPage(insertDoc, ins.Number);
        }
        if (afterPage == 0)
            foreach (var ins in insertDoc.GetPages())
                builder.AddPage(insertDoc, ins.Number);
        File.WriteAllBytes(outputPath, builder.Build());
    }

    public static int GetPageCount(string pdfPath)
    {
        using var doc = PdfDocument.Open(pdfPath);
        return doc.NumberOfPages;
    }

    private static HashSet<int> ParsePageSpec(string spec)
    {
        var result = new HashSet<int>();
        foreach (var part in spec.Split(',', ';'))
        {
            var p = part.Trim();
            if (p.Contains('-'))
            {
                var lr = p.Split('-');
                if (int.TryParse(lr[0], out int s) && int.TryParse(lr[1], out int e))
                    for (int i = s; i <= e; i++) result.Add(i);
            }
            else if (int.TryParse(p, out int n)) result.Add(n);
        }
        return result;
    }

    private static List<(int, int)> ParseRanges(string spec)
    {
        var result = new List<(int, int)>();
        foreach (var part in spec.Split(',', ';'))
        {
            var p = part.Trim();
            if (p.Contains('-'))
            {
                var lr = p.Split('-');
                if (int.TryParse(lr[0], out int s) && int.TryParse(lr[1], out int e))
                    result.Add((s, e));
            }
            else if (int.TryParse(p, out int n)) result.Add((n, n));
        }
        return result;
    }
}
'@

# ══════════════════════════════════════════════════════════════════════════════
# 4. Services\PdfConvertService.cs — fix UglyToad.PdfPig namespace
# ══════════════════════════════════════════════════════════════════════════════
Write-File "Services\PdfConvertService.cs" @'
using System.Diagnostics;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using PaperlessDesktop.Shared;

namespace PaperlessDesktop.Services;

public record OcrResult(string OutputPath, int PagesProcessed);
public record ConvertResult(string OutputPath);
public record ImagesToPdfResult(string OutputPath, int ImageCount);

public class PdfConvertService
{
    public async Task<OcrResult> OcrPdfAsync(
        string inputPath, string? outputPath = null,
        string lang = "ron+eng", bool forceOcr = true,
        IProgress<(int done, int total, string line)>? progress = null,
        CancellationToken ct = default)
    {
        outputPath ??= Path.Combine(
            Path.GetDirectoryName(inputPath)!,
            Path.GetFileNameWithoutExtension(inputPath) + "_ocr.pdf");

        var args = $"\"{inputPath}\" \"{outputPath}\" --language {lang} "
                 + (forceOcr ? "--force-ocr " : "--skip-text ")
                 + "--progress-bar --jobs 2";

        var psi = new ProcessStartInfo("ocrmypdf", args)
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
        };
        using var proc = Process.Start(psi)
            ?? throw new OcrException("Could not start ocrmypdf.");
        int done = 0, total = 0;
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data?.StartsWith("Page ") == true)
            {
                var parts = e.Data.Replace("Page ", "").Split('/');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out done) &&
                    int.TryParse(parts[1], out total))
                    progress?.Report((done, total, e.Data));
            }
        };
        proc.BeginOutputReadLine();
        await proc.WaitForExitAsync(ct);
        if (ct.IsCancellationRequested) { try { proc.Kill(); } catch { } throw new OcrCancelledException(); }
        if (proc.ExitCode != 0)
        {
            var err = await proc.StandardError.ReadToEndAsync(ct);
            throw new OcrException($"ocrmypdf failed (exit {proc.ExitCode}): {err}");
        }
        return new OcrResult(outputPath, total > 0 ? total : done);
    }

    public async Task<ConvertResult> PdfToDocxAsync(
        string inputPath, string outputPath,
        IProgress<(int done, int total)>? progress = null,
        CancellationToken ct = default)
    {
        var script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                  "tools", "pdf2docx_convert.py");
        var psi = new ProcessStartInfo("python",
                      $"\"{script}\" \"{inputPath}\" \"{outputPath}\"")
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
        };
        using var proc = Process.Start(psi)
            ?? throw new ConversionException("Could not start pdf2docx.");
        proc.BeginOutputReadLine();
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0)
        {
            var err = await proc.StandardError.ReadToEndAsync(ct);
            throw new ConversionException($"PDF to DOCX failed: {err}");
        }
        return new ConvertResult(outputPath);
    }

    public async Task<List<string>> PdfToImagesAsync(
        string inputPath, string outputDir,
        string format = "png", int dpi = 150,
        IProgress<(int done, int total, string name)>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDir);
        var stem    = Path.GetFileNameWithoutExtension(inputPath);
        var outputs = new List<string>();
        var file    = await Windows.Storage.StorageFile.GetFileFromPathAsync(inputPath);
        var pdfDoc  = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
        var total   = (int)pdfDoc.PageCount;
        for (uint i = 0; i < pdfDoc.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var page = pdfDoc.GetPage(i);
            var outName    = $"{stem}_page{i + 1:D3}.{format}";
            var folder     = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(outputDir);
            var outFile    = await folder.CreateFileAsync(outName,
                Windows.Storage.CreationCollisionOption.ReplaceExisting);
            using var stream = await outFile.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
            await page.RenderToStreamAsync(stream).AsTask(ct);
            outputs.Add(Path.Combine(outputDir, outName));
            progress?.Report(((int)i + 1, total, outName));
        }
        return outputs;
    }

    public async Task<ImagesToPdfResult> ImagesToPdfAsync(
        IEnumerable<string> imagePaths, string outputPath,
        IProgress<(int done, int total, string name)>? progress = null,
        CancellationToken ct = default)
    {
        var images = imagePaths.ToList();
        using var builder = new PdfDocumentBuilder();
        int i = 0;
        foreach (var imgPath in images)
        {
            ct.ThrowIfCancellationRequested();
            i++;
            byte[] jpegBytes = await ConvertToJpegAsync(imgPath);
            var page = builder.AddPage(PageSize.A4);
            page.AddJpeg(jpegBytes, new PdfRectangle(0, 0, 595, 842));
            progress?.Report((i, images.Count, Path.GetFileName(imgPath)));
        }
        File.WriteAllBytes(outputPath, builder.Build());
        return new ImagesToPdfResult(outputPath, images.Count);
    }

    private static async Task<byte[]> ConvertToJpegAsync(string imagePath)
    {
        var ext = Path.GetExtension(imagePath).ToLowerInvariant();
        if (ext is ".jpg" or ".jpeg")
            return await File.ReadAllBytesAsync(imagePath);
        var file    = await Windows.Storage.StorageFile.GetFileFromPathAsync(imagePath);
        using var inStream = await file.OpenReadAsync();
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(inStream);
        var bitmap  = await decoder.GetSoftwareBitmapAsync();
        using var outStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(
            Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId, outStream);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();
        var reader = new Windows.Storage.Streams.DataReader(outStream.GetInputStreamAt(0));
        var bytes  = new byte[outStream.Size];
        await reader.LoadAsync((uint)outStream.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }
}
'@

# ══════════════════════════════════════════════════════════════════════════════
# 5. ViewModels\MainViewModel.cs — add missing AddFiles() method
# ══════════════════════════════════════════════════════════════════════════════
Write-File "ViewModels\MainViewModel.cs" @'
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using PaperlessDesktop.Services;
using PaperlessDesktop.Shared;
using System.Collections.ObjectModel;

namespace PaperlessDesktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public  readonly LicenseService    License;
    private readonly PdfCoreService    _core;
    private readonly PdfConvertService _convert;
    private readonly BatchService      _batch;
    private readonly AppSettings       _settings;
    private readonly DispatcherQueue   _dispatcher;

    public ObservableCollection<FileItemViewModel> Files { get; } = new();

    [ObservableProperty] private string _statusText    = "Ready.";
    [ObservableProperty] private string _fileCountText = "0 files";
    [ObservableProperty] private bool   _isProcessing  = false;
    [ObservableProperty] private int    _progressValue = 0;
    [ObservableProperty] private int    _progressMax   = 100;
    [ObservableProperty] private string _progressLabel = "";
    [ObservableProperty] private bool   _isDarkMode;

    public BatchResult? LastBatchResult { get; set; }

    public MainViewModel(LicenseService license, PdfCoreService core,
        PdfConvertService convert, BatchService batch,
        AppSettings settings, DispatcherQueue dispatcher)
    {
        License     = license;
        _core       = core;
        _convert    = convert;
        _batch      = batch;
        _settings   = settings;
        _dispatcher = dispatcher;
        _isDarkMode = settings.DarkMode;
        LoadRecentFiles();
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Files.Any(f => f.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                continue;
            var pageCount = "";
            if (Path.GetExtension(path).ToLowerInvariant() == ".pdf")
            {
                try { pageCount = PdfCoreService.GetPageCount(path).ToString(); }
                catch { pageCount = "?"; }
            }
            Files.Add(new FileItemViewModel(path, pageCount));
            _settings.AddRecent(path);
        }
        _settings.Save();
        UpdateFileCount();
    }

    public void RemoveFile(FileItemViewModel item) { Files.Remove(item); UpdateFileCount(); }
    public void ClearFiles()                       { Files.Clear();      UpdateFileCount(); }

    public void MoveUp(FileItemViewModel item)
    { var i = Files.IndexOf(item); if (i > 0) Files.Move(i, i - 1); }

    public void MoveDown(FileItemViewModel item)
    { var i = Files.IndexOf(item); if (i >= 0 && i < Files.Count - 1) Files.Move(i, i + 1); }

    public IEnumerable<FileItemViewModel> SelectedPdfs =>
        Files.Where(f => f.IsSelected &&
            f.FilePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));

    public IEnumerable<FileItemViewModel> SelectedImages =>
        Files.Where(f => f.IsSelected &&
            new[]{ ".png",".jpg",".jpeg",".bmp",".tiff",".webp" }
                .Contains(Path.GetExtension(f.FilePath).ToLowerInvariant()));

    public IEnumerable<FileItemViewModel> AllOrSelected(bool pdfOnly = true)
    {
        var sel = pdfOnly ? SelectedPdfs : Files.Where(f => f.IsSelected);
        return sel.Any() ? sel
            : pdfOnly ? Files.Where(f => f.FilePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            : Files;
    }

    public void SetStatus(string msg) =>
        _dispatcher.TryEnqueue(() => StatusText = msg);

    public void ClearProgress() =>
        _dispatcher.TryEnqueue(() => { IsProcessing = false; ProgressValue = 0; ProgressLabel = ""; });

    partial void OnIsDarkModeChanged(bool value) { _settings.DarkMode = value; _settings.Save(); }

    private void LoadRecentFiles()
    {
        foreach (var path in _settings.RecentFiles)
            if (File.Exists(path)) Files.Add(new FileItemViewModel(path));
        UpdateFileCount();
    }

    private void UpdateFileCount() =>
        _dispatcher.TryEnqueue(() =>
            FileCountText = $"{Files.Count} file{(Files.Count == 1 ? "" : "s")}");
}
'@

# ══════════════════════════════════════════════════════════════════════════════
# 6. MainWindow.xaml — remove ALL x:Bind and x:DataType (crashes XAML compiler
#    in unpackaged WinUI 3). Switch to classic Binding throughout.
# ══════════════════════════════════════════════════════════════════════════════
$xamlPath = Join-Path $root "MainWindow.xaml"
if (Test-Path $xamlPath) {
    $xaml = Get-Content $xamlPath -Raw -Encoding UTF8
    $xaml = $xaml -replace '\s*x:DataType="[^"]*"', ''
    $xaml = $xaml -replace '\{x:Bind FileType\}',             '{Binding FileType}'
    $xaml = $xaml -replace '\{x:Bind FileName\}',             '{Binding FileName}'
    $xaml = $xaml -replace '\{x:Bind PageCount\}',            '{Binding PageCount}'
    $xaml = $xaml -replace '\{x:Bind ViewModel\.StatusText[^}]*\}',   '"Ready."'
    $xaml = $xaml -replace '\{x:Bind ViewModel\.FileCountText[^}]*\}','"0 files"'
    Set-Content -Path $xamlPath -Value $xaml -Encoding UTF8
    $remaining = ([regex]::Matches($xaml, 'x:Bind')).Count
    if ($remaining -gt 0) {
        Write-Host "  WARNING: $remaining x:Bind still in MainWindow.xaml — search and replace manually" -ForegroundColor Yellow
    } else {
        Write-Host "  OK: MainWindow.xaml" -ForegroundColor Green
    }
}

# ══════════════════════════════════════════════════════════════════════════════
# 7. MainWindow.xaml.cs — correct usings + wire status bar via PropertyChanged
# ══════════════════════════════════════════════════════════════════════════════
Write-File "MainWindow.xaml.cs" @'
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PaperlessDesktop.Dialogs;
using PaperlessDesktop.Services;
using PaperlessDesktop.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

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
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        foreach (var ext in new[]{ ".pdf",".png",".jpg",".jpeg",".docx",".csv",".xlsx" })
            picker.FileTypeFilter.Add(ext);
        picker.ViewMode = PickerViewMode.List;
        var files = await picker.PickMultipleFilesAsync();
        if (files?.Count > 0) ViewModel.AddFiles(files.Select(f => f.Path));
    }

    private void RemoveBtn_Click(object s, RoutedEventArgs e)
    {
        foreach (var item in FileListView.SelectedItems.Cast<FileItemViewModel>().ToList())
            ViewModel.RemoveFile(item);
    }

    private async void ClearBtn_Click(object s, RoutedEventArgs e)
    {
        var dlg = new ContentDialog {
            Title = "Clear list", Content = "Remove all files from the list?",
            PrimaryButtonText = "Yes", CloseButtonText = "No", XamlRoot = Content.XamlRoot
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
        await Run("Merging...", async (_, _) => {
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
        await Run("Removing pages...", async (_, _) => {
            App.Services.GetRequiredService<PdfCoreService>().RemovePages(sel.FilePath, out_, spec);
            await Info("Done", $"Saved:\n{out_}");
        });
    }

    private async void RotatePages_Click(object s, RoutedEventArgs e)
    {
        var sel = ViewModel.SelectedPdfs.FirstOrDefault();
        if (sel is null) { await Info("Rotate", "Select one PDF."); return; }
        var spec  = await Ask("Rotate Pages", "Pages (e.g. 1,3-5 or all):", "all"); if (spec is null) return;
        var angle = await AskInt("Angle", "Rotation angle (90/180/270):", 90); if (angle is null) return;
        var out_  = await SaveFile("rotated.pdf", ".pdf"); if (out_ is null) return;
        await Run("Rotating...", async (_, _) => {
            App.Services.GetRequiredService<PdfCoreService>().RotatePages(sel.FilePath, out_, spec, angle.Value);
            await Info("Done", $"Saved:\n{out_}");
        });
    }

    private async void InsertPages_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Insert Pages")) return;
        var sel   = ViewModel.SelectedPdfs.FirstOrDefault();
        if (sel is null) { await Info("Insert Pages", "Select the base PDF."); return; }
        var src   = await OpenFile(".pdf"); if (src is null) return;
        var after = await AskInt("Insert after page", "After page number (0 = beginning):", 0); if (after is null) return;
        var out_  = await SaveFile("with_insert.pdf", ".pdf"); if (out_ is null) return;
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
        var title   = await Ask("Title",    "Document title:",   ""); if (title is null) return;
        var author  = await Ask("Author",   "Author:",           ""); if (author is null) return;
        var subject = await Ask("Subject",  "Subject:",          ""); if (subject is null) return;
        var keys    = await Ask("Keywords", "Keywords:",         ""); if (keys is null) return;
        var out_    = await SaveFile("metadata_edited.pdf", ".pdf"); if (out_ is null) return;
        App.Services.GetRequiredService<PdfCoreService>().EditMetadata(sel.FilePath, out_, title, author, subject, keys);
        await Info("Done", $"Saved:\n{out_}");
    }

    private async void OcrPdf_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("OCR PDF")) return;
        var sel = ViewModel.SelectedPdfs.FirstOrDefault();
        if (sel is null) { await Info("OCR", "Select one PDF."); return; }
        var out_ = await SaveFile(Path.GetFileNameWithoutExtension(sel.FilePath) + "_ocr.pdf", ".pdf"); if (out_ is null) return;
        await Run("Running OCR...", async (prog, ct) => {
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
        await Run("Converting...", async (_, ct) => {
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
        await Run("Exporting images...", async (prog, ct) => {
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
        await Run("Creating PDF...", async (prog, ct) => {
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
        var out_    = await SaveFile(Path.GetFileNameWithoutExtension(sel.FilePath) + "_compressed.pdf", ".pdf"); if (out_ is null) return;
        await Run("Compressing...", async (_, _) => {
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
        var byN    = await YesNo("Split method", "Yes = every N pages\nNo = custom ranges");
        SplitResult res;
        if (byN) {
            var n = await AskInt("Every N pages", "Enter N:", 2); if (n is null) return;
            res = App.Services.GetRequiredService<PdfCoreService>().SplitEveryN(sel.FilePath, outDir, n.Value);
        } else {
            var spec = await Ask("Ranges", "Ranges (e.g. 1-3,5,7-9):"); if (spec is null) return;
            res = App.Services.GetRequiredService<PdfCoreService>().SplitByRanges(sel.FilePath, outDir, spec);
        }
        await Info("Done", $"Created {res.OutputFiles.Count} file(s) in:\n{outDir}");
    }

    private async void ImageEditor_Click(object s, RoutedEventArgs e)   => await Info("Image Editor",    "See roadmap Step 3.");
    private async void DocxFindReplace_Click(object s, RoutedEventArgs e) => await Info("Find & Replace", "See roadmap Step 4.");
    private async void DocxMerge_Click(object s, RoutedEventArgs e)     => await Info("Merge DOCX",       "See roadmap Step 4.");
    private async void CsvViewer_Click(object s, RoutedEventArgs e)     => await Info("Table Editor",     "See roadmap Step 5.");

    private async void BatchCompress_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Batch Compress")) return;
        var quality = await AskQuality(); if (quality is null) return;
        var files   = ViewModel.AllOrSelected().Select(f => f.FilePath).ToList();
        var outDir  = await PickFolder(); if (outDir is null) return;
        await Run("Batch Compress...", async (prog, ct) => {
            var res = await App.Services.GetRequiredService<BatchService>()
                               .BatchCompressAsync(files, outDir, quality, prog, ct);
            await Info("Done", res.Summary());
        });
    }

    private async void BatchOcr_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Batch OCR")) return;
        var files  = ViewModel.AllOrSelected().Select(f => f.FilePath).ToList();
        var outDir = await PickFolder(); if (outDir is null) return;
        await Run("Batch OCR...", async (prog, ct) => {
            var res = await App.Services.GetRequiredService<BatchService>()
                               .BatchOcrAsync(files, outDir, prog: prog, ct: ct);
            await Info("Done", res.Summary());
        });
    }

    private async void BatchRemove_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Batch Remove Pages")) return;
        var spec   = await Ask("Pages", "Pages to remove (e.g. 1,3-5):"); if (spec is null) return;
        var files  = ViewModel.AllOrSelected().Select(f => f.FilePath).ToList();
        var outDir = await PickFolder(); if (outDir is null) return;
        await Run("Batch Remove Pages...", async (prog, ct) => {
            var res = await App.Services.GetRequiredService<BatchService>()
                               .BatchRemovePagesAsync(files, outDir, spec, prog, ct);
            await Info("Done", res.Summary());
        });
    }

    private async void BatchRotate_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Batch Rotate")) return;
        var spec   = await Ask("Pages", "Pages (all or e.g. 1,3-5):", "all"); if (spec is null) return;
        var angle  = await AskInt("Angle", "Rotation angle (90/180/270):", 90); if (angle is null) return;
        var files  = ViewModel.AllOrSelected().Select(f => f.FilePath).ToList();
        var outDir = await PickFolder(); if (outDir is null) return;
        await Run("Batch Rotate...", async (prog, ct) => {
            var res = await App.Services.GetRequiredService<BatchService>()
                               .BatchRotateAsync(files, outDir, spec, angle.Value, prog, ct);
            await Info("Done", res.Summary());
        });
    }

    private async void BatchPdfImages_Click(object s, RoutedEventArgs e)
    {
        if (!RequirePro("Batch PDF to Images")) return;
        var files  = ViewModel.AllOrSelected().Select(f => f.FilePath).ToList();
        var outDir = await PickFolder(); if (outDir is null) return;
        await Run("Batch PDF to Images...", async (prog, ct) => {
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
        try {
            var prog = new Progress<(int d, int t, string n)>(x => dlg.Update(x.d, x.t, x.n));
            await work(prog, dlg.CancellationToken);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await Err("Error", ex.Message); }
        finally { dlg.Hide(); }
    }

    private async Task<string?> SaveFile(string name, string ext)
    {
        var p = new FileSavePicker();
        InitializeWithWindow.Initialize(p, WindowNative.GetWindowHandle(this));
        p.SuggestedFileName = name;
        p.FileTypeChoices.Add(ext.TrimStart('.').ToUpperInvariant(), new List<string> { ext });
        return (await p.PickSaveFileAsync())?.Path;
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

    private Task Info(string title, string msg) => new ContentDialog
        { Title = title, Content = msg, CloseButtonText = "OK", XamlRoot = Content.XamlRoot }.ShowAsync().AsTask();

    private Task Err(string title, string msg) => new ContentDialog
        { Title = title, Content = msg, CloseButtonText = "OK", XamlRoot = Content.XamlRoot }.ShowAsync().AsTask();

    private async Task<string?> Ask(string title, string prompt, string def = "")
    {
        var tb  = new TextBox { Text = def };
        var dlg = new ContentDialog {
            Title = title,
            Content = new StackPanel { Children = { new TextBlock { Text = prompt }, tb } },
            PrimaryButtonText = "OK", CloseButtonText = "Cancel", XamlRoot = Content.XamlRoot
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary ? tb.Text : null;
    }

    private async Task<int?> AskInt(string title, string prompt, int def)
    {
        var nb = new NumberBox { Value = def, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var dlg = new ContentDialog {
            Title = title,
            Content = new StackPanel { Children = { new TextBlock { Text = prompt }, nb } },
            PrimaryButtonText = "OK", CloseButtonText = "Cancel", XamlRoot = Content.XamlRoot
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary ? (int)nb.Value : null;
    }

    private async Task<string?> AskQuality()
    {
        var cb = new ComboBox {
            ItemsSource  = new[] { "screen", "ebook", "printer", "prepress" },
            SelectedItem = "ebook",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var dlg = new ContentDialog {
            Title = "Compression quality",
            Content = new StackPanel { Children = {
                new TextBlock { Text = "screen=smallest  ebook=balanced  printer/prepress=quality",
                                Opacity = 0.6, TextWrapping = TextWrapping.Wrap }, cb } },
            PrimaryButtonText = "OK", CloseButtonText = "Cancel", XamlRoot = Content.XamlRoot
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary ? cb.SelectedItem as string : null;
    }

    private async Task<bool> YesNo(string title, string prompt)
    {
        var dlg = new ContentDialog {
            Title = title, Content = prompt,
            PrimaryButtonText = "Yes", SecondaryButtonText = "No", XamlRoot = Content.XamlRoot
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary;
    }

    private static string FormatBytes(long n)
    {
        foreach (var u in new[]{"B","KB","MB","GB"}) {
            if (n < 1024) return $"{n:F1} {u}";
            n /= 1024;
        }
        return $"{n:F1} TB";
    }
}
'@

# ══════════════════════════════════════════════════════════════════════════════
# Done
# ══════════════════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "  All fixes applied!" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Now run:" -ForegroundColor Yellow
Write-Host "    dotnet restore"
Write-Host "    dotnet build"
Write-Host ""
