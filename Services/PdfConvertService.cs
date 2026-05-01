using PaperlessDesktop.Shared;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using PdfSharpCore.Drawing;

namespace PaperlessDesktop.Services;

public record OcrResult(string OutputPath, int PagesProcessed);
public record ConvertResult(string OutputPath);
public record ImagesToPdfResult(string OutputPath, int ImageCount);

public class PdfConvertService
{
    // ── OCR (Windows.Media.Ocr — no external tools) ─────────────────────────

    public async Task<OcrResult> OcrPdfAsync(
        string inputPath, string? outputPath = null,
        string lang = "en", bool forceOcr = true,
        IProgress<(int done, int total, string line)>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(inputPath))
                throw new OcrException($"Input file not found: {inputPath}");

            outputPath ??= Path.Combine(
                Path.GetDirectoryName(inputPath)!,
                Path.GetFileNameWithoutExtension(inputPath) + "_ocr.pdf");

            Logger.Info($"Starting OCR on {inputPath} using Windows.Media.Ocr");

            // Determine OCR language
            var ocrLang = new Windows.Globalization.Language("en-US");
            if (lang.StartsWith("ron", StringComparison.OrdinalIgnoreCase))
                ocrLang = new Windows.Globalization.Language("ro");
            else if (lang.StartsWith("de", StringComparison.OrdinalIgnoreCase))
                ocrLang = new Windows.Globalization.Language("de");
            else if (lang.StartsWith("fr", StringComparison.OrdinalIgnoreCase))
                ocrLang = new Windows.Globalization.Language("fr");

            if (!Windows.Media.Ocr.OcrEngine.IsLanguageSupported(ocrLang))
            {
                // Fallback to first available
                var available = Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages;
                ocrLang = available.FirstOrDefault()
                    ?? throw new OcrException("No OCR languages are installed on this system. Install a language pack in Windows Settings → Language.");
            }

            var ocrEngine = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(ocrLang)
                ?? throw new OcrException($"Could not create OCR engine for language: {ocrLang.DisplayName}");

            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(inputPath);
            var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
            int totalPages = (int)pdfDoc.PageCount;

            using var outPdf = new PdfSharpCore.Pdf.PdfDocument();

            for (uint i = 0; i < pdfDoc.PageCount; i++)
            {
                ct.ThrowIfCancellationRequested();

                using var pdfPage = pdfDoc.GetPage(i);
                using var stream  = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                await pdfPage.RenderToStreamAsync(stream).AsTask(ct);

                // Decode for OCR
                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
                var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                    Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                    Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);

                var ocrResult = await ocrEngine.RecognizeAsync(softwareBitmap);

                double pageWidthPt  = pdfPage.Size.Width  * 72.0 / 96.0;
                double pageHeightPt = pdfPage.Size.Height * 72.0 / 96.0;
                double imgWidth     = decoder.PixelWidth;
                double imgHeight    = decoder.PixelHeight;

                // Transcode to JPEG
                using var jpegStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(
                    Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId, jpegStream);
                encoder.SetSoftwareBitmap(softwareBitmap);
                await encoder.FlushAsync();
                var jpegReader = new Windows.Storage.Streams.DataReader(jpegStream.GetInputStreamAt(0));
                await jpegReader.LoadAsync((uint)jpegStream.Size);
                var jpegBytes = new byte[jpegStream.Size];
                jpegReader.ReadBytes(jpegBytes);

                // Create PDF page with image background
                var outPage = outPdf.AddPage();
                outPage.Width  = XUnit.FromPoint(pageWidthPt);
                outPage.Height = XUnit.FromPoint(pageHeightPt);

                using var gfx = XGraphics.FromPdfPage(outPage);
                using var ms  = new MemoryStream(jpegBytes);
                var img = XImage.FromStream(() => new MemoryStream(jpegBytes));
                gfx.DrawImage(img, 0, 0, pageWidthPt, pageHeightPt);

                // Invisible text layer for searchability
                if (ocrResult?.Text?.Length > 0)
                {
                    var font = new XFont("Arial", 1, XFontStyle.Regular);
                    foreach (var line in ocrResult.Lines)
                    {
                        foreach (var word in line.Words)
                        {
                            double x  = word.BoundingRect.X      / imgWidth  * pageWidthPt;
                            double y  = word.BoundingRect.Y      / imgHeight * pageHeightPt;
                            double fs = word.BoundingRect.Height / imgHeight * pageHeightPt * 0.9;
                            if (fs < 1) fs = 6; if (fs > 72) fs = 72;
                            var wFont = new XFont("Arial", fs, XFontStyle.Regular);
                            // Draw with transparent brush so text is searchable but invisible
                            gfx.DrawString(word.Text, wFont, XBrushes.Transparent, new XPoint(x, y));
                        }
                    }
                }

                progress?.Report(((int)i + 1, totalPages, $"Page {i + 1}/{totalPages}"));
            }

            outPdf.Save(outputPath);
            Logger.Info($"OCR completed: {outputPath} ({totalPages} pages)");
            return new OcrResult(outputPath, totalPages);
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

    // ── PDF → DOCX (PdfPig text extraction + OpenXml) ───────────────────────

    public async Task<ConvertResult> PdfToDocxAsync(
        string inputPath, string outputPath,
        IProgress<(int done, int total)>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(inputPath))
                throw new ConversionException($"Input file not found: {inputPath}");

            Logger.Info($"Starting PDF to DOCX conversion: {inputPath}");

            await Task.Run(() =>
            {
                using var pdfDoc = UglyToad.PdfPig.PdfDocument.Open(inputPath);
                int totalPages = pdfDoc.NumberOfPages;

                using var wordDoc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
                var mainPart = wordDoc.AddMainDocumentPart();
                mainPart.Document = new Document();
                var body = mainPart.Document.AppendChild(new Body());

                int pageNum = 0;
                foreach (var page in pdfDoc.GetPages())
                {
                    ct.ThrowIfCancellationRequested();
                    pageNum++;

                    // Add page header
                    var headerPara = new Paragraph(
                        new Run(
                            new RunProperties(new Bold(), new FontSize { Val = "24" }),
                            new Text($"— Page {pageNum} —")));
                    body.AppendChild(headerPara);

                    // Extract text and add as paragraphs
                    var text = page.Text ?? "";
                    var lines = text.Split('\n');
                    foreach (var line in lines)
                    {
                        var trimmed = line.TrimEnd('\r');
                        var para = new Paragraph(new Run(new Text(trimmed) { Space = SpaceProcessingModeValues.Preserve }));
                        body.AppendChild(para);
                    }

                    // Add page break (except after last page)
                    if (pageNum < totalPages)
                    {
                        var breakPara = new Paragraph(
                            new Run(new Break { Type = BreakValues.Page }));
                        body.AppendChild(breakPara);
                    }

                    progress?.Report((pageNum, totalPages));
                }
            }, ct);

            Logger.Info($"PDF to DOCX completed: {outputPath}");
            return new ConvertResult(outputPath);
        }
        catch (Exception ex)
        {
            Logger.Error("PdfToDocxAsync failed", ex);
            throw;
        }
    }

    // ── PDF → Images ─────────────────────────────────────────────────────────

    public async Task<List<string>> PdfToImagesAsync(
        string inputPath, string outputDir,
        string format = "png", int dpi = 150,
        IProgress<(int done, int total, string name)>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            Logger.Info($"Starting PDF to Images: {inputPath}");
            Directory.CreateDirectory(outputDir);

            var stem    = Path.GetFileNameWithoutExtension(inputPath);
            var outputs = new List<string>();

            var file   = await Windows.Storage.StorageFile.GetFileFromPathAsync(inputPath);
            var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
            var total  = (int)pdfDoc.PageCount;

            for (uint i = 0; i < pdfDoc.PageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var page  = pdfDoc.GetPage(i);
                    var outName     = $"{stem}_page{i + 1:D3}.{format}";
                    var folder      = await Windows.Storage.StorageFolder
                                           .GetFolderFromPathAsync(outputDir);
                    var outFile     = await folder.CreateFileAsync(outName,
                                           Windows.Storage.CreationCollisionOption.ReplaceExisting);
                    using var stream = await outFile.OpenAsync(
                                           Windows.Storage.FileAccessMode.ReadWrite);
                    await page.RenderToStreamAsync(stream).AsTask(ct);
                    outputs.Add(Path.Combine(outputDir, outName));
                    progress?.Report(((int)i + 1, total, outName));
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to convert page {i + 1}: {ex.Message}");
                }
            }

            Logger.Info($"PDF to Images completed: {outputs.Count} images");
            return outputs;
        }
        catch (Exception ex)
        {
            Logger.Error("PdfToImagesAsync failed", ex);
            throw;
        }
    }

    // ── Images → PDF (FIXED) ─────────────────────────────────────────────────

    /// <summary>
    /// Converts image files into a single PDF.
    /// Each image becomes its own page, sized to the image dimensions (in PDF points).
    /// PNG / BMP / TIFF are transcoded to JPEG via Windows.Graphics.Imaging.
    /// </summary>
    public async Task<ImagesToPdfResult> ImagesToPdfAsync(
        IEnumerable<string> imagePaths, string outputPath,
        IProgress<(int done, int total, string name)>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            Logger.Info("Starting Images to PDF conversion");
            var images = imagePaths.ToList();
            using var builder = new PdfDocumentBuilder();
            int i = 0;

            foreach (var imgPath in images)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    i++;
                    var name = Path.GetFileName(imgPath);

                    // Decode image → JPEG bytes + page dimensions in PDF points
                    var (jpegBytes, widthPt, heightPt) = await PrepareImageAsync(imgPath);

                    // Add a page sized to the image and embed the JPEG
                    var page = builder.AddPage(widthPt, heightPt);
                    page.AddJpeg(jpegBytes, new UglyToad.PdfPig.Core.PdfRectangle(
                        0, 0, widthPt, heightPt));

                    Logger.Info($"Added image {i}/{images.Count}: {name}");
                    progress?.Report((i, images.Count, name));
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Skipped image {imgPath}: {ex.Message}");
                }
            }

            File.WriteAllBytes(outputPath, builder.Build());
            Logger.Info($"Images to PDF completed: {outputPath} ({i} images)");
            return new ImagesToPdfResult(outputPath, i);
        }
        catch (Exception ex)
        {
            Logger.Error("ImagesToPdfAsync failed", ex);
            throw;
        }
    }

    // ── Helper: decode any image → JPEG bytes + page size in PDF points ───────

    private static async Task<(byte[] jpegBytes, double widthPt, double heightPt)>
        PrepareImageAsync(string imagePath)
    {
        const double DpiToPt = 72.0;

        var file      = await Windows.Storage.StorageFile.GetFileFromPathAsync(imagePath);
        using var inStream = await file.OpenReadAsync();

        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(inStream);

        // Use embedded DPI, fallback to 96 dpi (screen default)
        double dpiX = decoder.DpiX > 0 ? decoder.DpiX : 96.0;
        double dpiY = decoder.DpiY > 0 ? decoder.DpiY : 96.0;

        double widthPt  = decoder.PixelWidth  * DpiToPt / dpiX;
        double heightPt = decoder.PixelHeight * DpiToPt / dpiY;

        byte[] jpegBytes;
        var ext = Path.GetExtension(imagePath).ToLowerInvariant();

        if (ext is ".jpg" or ".jpeg")
        {
            // Native JPEG — no transcode needed
            jpegBytes = await File.ReadAllBytesAsync(imagePath);
        }
        else
        {
            // Transcode to JPEG via Windows Imaging
            var bitmap  = await decoder.GetSoftwareBitmapAsync();
            using var outStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(
                Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId, outStream);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();

            var reader = new Windows.Storage.Streams.DataReader(outStream.GetInputStreamAt(0));
            jpegBytes  = new byte[outStream.Size];
            await reader.LoadAsync((uint)outStream.Size);
            reader.ReadBytes(jpegBytes);
        }

        return (jpegBytes, widthPt, heightPt);
    }
}
