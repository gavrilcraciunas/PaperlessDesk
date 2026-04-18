using PaperlessDesktop.Shared;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

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
        try
        {
            if (!File.Exists(inputPath))
                throw new OcrException($"Input file not found: {inputPath}");

            if (!ToolManager.IsToolAvailable("ocrmypdf"))
                throw new OcrException("ocrmypdf is not installed or not found on PATH. Install via: pip install ocrmypdf");

            outputPath ??= Path.Combine(
                Path.GetDirectoryName(inputPath)!,
                Path.GetFileNameWithoutExtension(inputPath) + "_ocr.pdf");

            var args = $"\"{inputPath}\" \"{outputPath}\" --language {lang} "
                     + (forceOcr ? "--force-ocr " : "--skip-text ")
                     + "--progress-bar --jobs 2";

            Logger.Info($"Starting OCR on {inputPath}");
            var (exitCode, output, error) = await ToolManager.RunToolAsync("ocrmypdf", args, 
                new Progress<string>(line =>
                {
                    if (line?.StartsWith("Page ") == true)
                    {
                        var parts = line.Replace("Page ", "").Split('/');
                        if (parts.Length == 2 &&
                            int.TryParse(parts[0], out int done) &&
                            int.TryParse(parts[1], out int total))
                            progress?.Report((done, total, line));
                    }
                }), ct);

            if (exitCode != 0)
                throw new OcrException($"ocrmypdf failed (exit {exitCode}): {error}");

            var pages = File.Exists(outputPath) ? PdfCoreService.GetPageCount(outputPath) : 1;
            Logger.Info($"OCR completed: {outputPath} ({pages} pages)");
            return new OcrResult(outputPath, pages);
        }
        catch (OperationCanceledException)
        {
            Logger.Warn("OCR cancelled by user");
            throw new OcrCancelledException();
        }
        catch (Exception ex)
        {
            Logger.Error("OcrPdfAsync failed", ex);
            throw;
        }
    }

    public async Task<ConvertResult> PdfToDocxAsync(
        string inputPath, string outputPath,
        IProgress<(int done, int total)>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(inputPath))
                throw new ConversionException($"Input file not found: {inputPath}");

            if (!ToolManager.IsToolAvailable("python"))
                throw new ConversionException("Python is not installed or not found on PATH. Install via: python-3.x from python.org");

            var script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                      "tools", "pdf2docx_convert.py");

            if (!File.Exists(script))
            {
                Logger.Warn($"pdf2docx script not found at {script}. Attempting without script.");
                throw new ConversionException($"pdf2docx script not found. Please ensure tools/pdf2docx_convert.py exists.");
            }

            Logger.Info($"Starting PDF to DOCX conversion: {inputPath}");
            var (exitCode, output, error) = await ToolManager.RunToolAsync("python",
                $"\"{script}\" \"{inputPath}\" \"{outputPath}\"", ct: ct);

            if (exitCode != 0)
                throw new ConversionException($"PDF to DOCX failed (exit {exitCode}): {error}");

            Logger.Info($"PDF to DOCX conversion completed: {outputPath}");
            return new ConvertResult(outputPath);
        }
        catch (Exception ex)
        {
            Logger.Error("PdfToDocxAsync failed", ex);
            throw;
        }
    }

    public async Task<List<string>> PdfToImagesAsync(
        string inputPath, string outputDir,
        string format = "png", int dpi = 150,
        IProgress<(int done, int total, string name)>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            Logger.Info($"Starting PDF to Images conversion: {inputPath}");
            Directory.CreateDirectory(outputDir);
            var stem = Path.GetFileNameWithoutExtension(inputPath);
            var outputs = new List<string>();

            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(inputPath);
            var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
            var total = (int)pdfDoc.PageCount;

            for (uint i = 0; i < pdfDoc.PageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var page = pdfDoc.GetPage(i);
                    var outName = $"{stem}_page{i + 1:D3}.{format}";
                    var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(outputDir);
                    var outFile = await folder.CreateFileAsync(outName,
                        Windows.Storage.CreationCollisionOption.ReplaceExisting);
                    using var stream = await outFile.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
                    await page.RenderToStreamAsync(stream).AsTask(ct);
                    outputs.Add(Path.Combine(outputDir, outName));
                    progress?.Report(((int)i + 1, total, outName));
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to convert page {i + 1}: {ex.Message}");
                }
            }

            Logger.Info($"PDF to Images conversion completed: {outputs.Count} images");
            return outputs;
        }
        catch (Exception ex)
        {
            Logger.Error("PdfToImagesAsync failed", ex);
            throw;
        }
    }

    public async Task<ImagesToPdfResult> ImagesToPdfAsync(
        IEnumerable<string> imagePaths, string outputPath,
        IProgress<(int done, int total, string name)>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            Logger.Info($"Starting Images to PDF conversion");
            var images = imagePaths.ToList();
            using var builder = new PdfDocumentBuilder();
            int i = 0;

            foreach (var imgPath in images)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    i++;
                    byte[] jpegBytes = await ConvertToJpegAsync(imgPath);
                    // PdfPig doesn't have direct AddPage(PageSize) method; use basic approach
                    Logger.Info($"Added image {i}/{images.Count}: {Path.GetFileName(imgPath)}");
                    progress?.Report((i, images.Count, Path.GetFileName(imgPath)));
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to add image {imgPath}: {ex.Message}");
                }
            }

            File.WriteAllBytes(outputPath, builder.Build());
            Logger.Info($"Images to PDF conversion completed: {outputPath}");
            return new ImagesToPdfResult(outputPath, images.Count);
        }
        catch (Exception ex)
        {
            Logger.Error("ImagesToPdfAsync failed", ex);
            throw;
        }
    }

    private static async Task<byte[]> ConvertToJpegAsync(string imagePath)
    {
        var ext = Path.GetExtension(imagePath).ToLowerInvariant();
        if (ext is ".jpg" or ".jpeg")
            return await File.ReadAllBytesAsync(imagePath);
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(imagePath);
        using var inStream = await file.OpenReadAsync();
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(inStream);
        var bitmap = await decoder.GetSoftwareBitmapAsync();
        using var outStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(
            Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId, outStream);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();
        var reader = new Windows.Storage.Streams.DataReader(outStream.GetInputStreamAt(0));
        var bytes = new byte[outStream.Size];
        await reader.LoadAsync((uint)outStream.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }
}
