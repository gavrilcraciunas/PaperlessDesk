using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

using iText.Kernel.Pdf;
using iText.Kernel.Utils;
using iText.Kernel.Pdf.Canvas;
using iText.Layout;

// ✅ ALIASES (fix ambiguity)
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;
using iTextPdfDocument = iText.Kernel.Pdf.PdfDocument;
using iTextReader = iText.Kernel.Pdf.PdfReader;
using iTextWriter = iText.Kernel.Pdf.PdfWriter;

public class PdfCoreService
{
    // ── Result types ─────────────────────────────────────────────────────────

    public class SplitResult
    {
        public List<string> OutputFiles { get; set; } = new();
    }

    public class RemoveResult
    {
        public string OutputPath { get; set; } = "";
        public int RemovedCount { get; set; }
    }

    public class CompressResult
    {
        public long OriginalSize { get; set; }
        public long OutputSize { get; set; }
        public double SavingsPct => OriginalSize > 0 ? (1.0 - (double)OutputSize / OriginalSize) * 100 : 0;
        public bool KeptOriginal { get; set; }
    }

    // ── GET PAGE COUNT (static, PdfPig) ──────────────────────────────────────

    public static int GetPageCount(string filePath)
    {
        using var doc = PdfPigDocument.Open(filePath);
        return doc.NumberOfPages;
    }

    // ── SPLIT EVERY N (PdfPig) ───────────────────────────────────────────────

    public SplitResult SplitEveryN(string inputPath, string outputDir, int n)
    {
        var result = new SplitResult();
        Directory.CreateDirectory(outputDir);

        using var doc = PdfPigDocument.Open(inputPath);
        int totalPages = doc.NumberOfPages;
        int fileIndex = 1;

        for (int i = 1; i <= totalPages; i += n)
        {
            var builder = new PdfDocumentBuilder();
            int end = Math.Min(i + n - 1, totalPages);

            for (int p = i; p <= end; p++)
                builder.AddPage(doc, p);

            string outPath = Path.Combine(outputDir, $"split_{fileIndex}.pdf");
            File.WriteAllBytes(outPath, builder.Build());
            result.OutputFiles.Add(outPath);
            fileIndex++;
        }

        return result;
    }

    // ── SPLIT BY RANGES (PdfPig) ────────────────────────────────────────────

    public SplitResult SplitByRanges(string inputPath, string outputDir, string ranges)
    {
        var result = new SplitResult();
        Directory.CreateDirectory(outputDir);

        using var doc = PdfPigDocument.Open(inputPath);
        var parsed = ParseRanges(ranges);
        int index = 1;

        foreach (var (start, end) in parsed)
        {
            var builder = new PdfDocumentBuilder();
            for (int p = start; p <= end; p++)
                builder.AddPage(doc, p);

            string outPath = Path.Combine(outputDir, $"range_{index}.pdf");
            File.WriteAllBytes(outPath, builder.Build());
            result.OutputFiles.Add(outPath);
            index++;
        }

        return result;
    }

    // ── MERGE PDFs (iText) ──────────────────────────────────────────────────

    public string MergePdfs(IEnumerable<string> inputFiles, string outputPath)
    {
        using var writer = new iTextWriter(outputPath);
        using var pdf = new iTextPdfDocument(writer);
        var merger = new iText.Kernel.Utils.PdfMerger(pdf);

        foreach (var file in inputFiles)
        {
            using var reader = new iTextReader(file);
            using var source = new iTextPdfDocument(reader);
            merger.Merge(source, 1, source.GetNumberOfPages());
        }

        return outputPath;
    }

    // ── REMOVE PAGES (string spec overload) ─────────────────────────────────

    public RemoveResult RemovePages(string inputPath, string outputPath, string pagesSpec)
    {
        var pagesToRemove = ParsePageSpec(inputPath, pagesSpec);
        return RemovePages(inputPath, outputPath, pagesToRemove);
    }

    public RemoveResult RemovePages(string inputPath, string outputPath, List<int> pagesToRemove)
    {
        using var reader = new iTextReader(inputPath);
        using var writer = new iTextWriter(outputPath);
        using var pdf = new iTextPdfDocument(reader, writer);

        int totalPages = pdf.GetNumberOfPages();
        var sorted = pagesToRemove
            .Where(p => p >= 1 && p <= totalPages)
            .Distinct()
            .OrderByDescending(p => p)
            .ToList();

        foreach (var p in sorted)
            pdf.RemovePage(p);

        int removed = sorted.Count;
        pdf.Close();
        return new RemoveResult { OutputPath = outputPath, RemovedCount = removed };
    }

    // ── ROTATE PAGES (iText) ────────────────────────────────────────────────

    public string RotatePages(string inputPath, string outputPath, string pagesSpec, int angle)
    {
        using var reader = new iTextReader(inputPath);
        using var writer = new iTextWriter(outputPath);
        using var pdf = new iTextPdfDocument(reader, writer);

        int totalPages = pdf.GetNumberOfPages();
        var pages = pagesSpec.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)
            ? Enumerable.Range(1, totalPages).ToList()
            : ParsePageSpec(inputPath, pagesSpec);

        foreach (var p in pages.Where(p => p >= 1 && p <= totalPages))
        {
            var page = pdf.GetPage(p);
            int current = page.GetRotation();
            page.SetRotation((current + angle) % 360);
        }

        return outputPath;
    }

    // ── INSERT PAGES (iText) ────────────────────────────────────────────────

    public string InsertPages(string inputPath, string sourcePath, int afterPage, string outputPath)
    {
        // Copy the input file first, then insert
        File.Copy(inputPath, outputPath, true);

        using var reader = new iTextReader(outputPath);
        reader.SetUnethicalReading(true);
        using var writer = new iTextWriter(outputPath + ".tmp");
        using var pdf = new iTextPdfDocument(reader, writer);

        using var sourceReader = new iTextReader(sourcePath);
        using var sourceDoc = new iTextPdfDocument(sourceReader);

        int sourcePages = sourceDoc.GetNumberOfPages();
        int insertAt = Math.Min(afterPage, pdf.GetNumberOfPages());

        for (int i = 1; i <= sourcePages; i++)
        {
            var page = sourceDoc.GetPage(i).CopyTo(pdf);
            pdf.AddPage(insertAt + i, page);
        }

        pdf.Close();
        sourceDoc.Close();

        File.Delete(outputPath);
        File.Move(outputPath + ".tmp", outputPath);

        return outputPath;
    }

    // ── PASSWORD PROTECT (iText) ────────────────────────────────────────────

    public string PasswordProtect(string inputPath, string outputPath, string userPwd, string ownerPwd)
    {
        var writerProps = new WriterProperties()
            .SetStandardEncryption(
                Encoding.UTF8.GetBytes(userPwd),
                Encoding.UTF8.GetBytes(ownerPwd),
                EncryptionConstants.ALLOW_PRINTING |
                EncryptionConstants.ALLOW_COPY,
                EncryptionConstants.ENCRYPTION_AES_256
            );

        using var reader = new iTextReader(inputPath);
        using var writer = new iTextWriter(outputPath, writerProps);
        using var pdf = new iTextPdfDocument(reader, writer);

        return outputPath;
    }

    // ── UNLOCK PDF (iText) ──────────────────────────────────────────────────

    public string UnlockPdf(string inputPath, string outputPath, string password)
    {
        var readerProps = new ReaderProperties()
            .SetPassword(Encoding.UTF8.GetBytes(password));

        using var reader = new iTextReader(inputPath, readerProps);
        using var writer = new iTextWriter(outputPath);
        using var pdf = new iTextPdfDocument(reader, writer);

        return outputPath;
    }

    // ── EDIT METADATA (iText) ───────────────────────────────────────────────

    public string EditMetadata(string inputPath, string outputPath, string title, string author, string subject, string keywords)
    {
        using var reader = new iTextReader(inputPath);
        using var writer = new iTextWriter(outputPath);
        using var pdf = new iTextPdfDocument(reader, writer);

        var info = pdf.GetDocumentInfo();
        if (!string.IsNullOrWhiteSpace(title)) info.SetTitle(title);
        if (!string.IsNullOrWhiteSpace(author)) info.SetAuthor(author);
        if (!string.IsNullOrWhiteSpace(subject)) info.SetSubject(subject);
        if (!string.IsNullOrWhiteSpace(keywords)) info.SetKeywords(keywords);

        return outputPath;
    }

    // ── COMPRESS PDF (iText) ────────────────────────────────────────────────

    public CompressResult CompressPdf(string inputPath, string outputPath, string? quality = "ebook")
    {
        long originalSize = new FileInfo(inputPath).Length;

        var writerProps = new WriterProperties()
            .SetFullCompressionMode(true)
            .SetCompressionLevel(CompressionConstants.BEST_COMPRESSION);

        using (var reader = new iTextReader(inputPath))
        using (var writer = new iTextWriter(outputPath, writerProps))
        using (var pdf = new iTextPdfDocument(reader, writer))
        {
            // Walk through all pages and compress streams
            for (int i = 1; i <= pdf.GetNumberOfPages(); i++)
            {
                var page = pdf.GetPage(i);
                page.GetPdfObject().Flush(true);
            }
        }

        long outputSize = new FileInfo(outputPath).Length;

        // If compression made the file bigger, keep original
        if (outputSize >= originalSize)
        {
            File.Copy(inputPath, outputPath, true);
            return new CompressResult
            {
                OriginalSize = originalSize,
                OutputSize = originalSize,
                KeptOriginal = true
            };
        }

        return new CompressResult
        {
            OriginalSize = originalSize,
            OutputSize = outputSize,
            KeptOriginal = false
        };
    }

    // ── EXTRACT TEXT (PdfPig) ───────────────────────────────────────────────

    public string ExtractText(string filePath)
    {
        using var doc = PdfPigDocument.Open(filePath);
        var sb = new StringBuilder();
        foreach (var page in doc.GetPages())
            sb.AppendLine(page.Text);
        return sb.ToString();
    }

    // ── HELPERS ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a page specification string like "1,3-5,7" into a list of page numbers.
    /// Also supports "all".
    /// </summary>
    private List<int> ParsePageSpec(string inputPath, string spec)
    {
        if (spec.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            int total = GetPageCount(inputPath);
            return Enumerable.Range(1, total).ToList();
        }

        var result = new List<int>();
        var parts = spec.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.Contains('-'))
            {
                var range = part.Split('-');
                int start = int.Parse(range[0].Trim());
                int end = int.Parse(range[1].Trim());
                for (int i = start; i <= end; i++)
                    result.Add(i);
            }
            else
            {
                result.Add(int.Parse(part.Trim()));
            }
        }
        return result.Distinct().ToList();
    }

    private List<(int start, int end)> ParseRanges(string input)
    {
        var result = new List<(int, int)>();
        var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.Contains('-'))
            {
                var range = part.Split('-');
                int start = int.Parse(range[0].Trim());
                int end = int.Parse(range[1].Trim());
                result.Add((start, end));
            }
            else
            {
                int page = int.Parse(part.Trim());
                result.Add((page, page));
            }
        }
        return result;
    }
}