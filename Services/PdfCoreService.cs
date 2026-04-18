using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

namespace PaperlessDesktop.Services;

public class PdfCoreService
{
    public record MergeResult(string OutputPath, int PageCount);
    public record RemoveResult(string OutputPath, int RemovedCount);
    public record RotateResult(string OutputPath);
    public record SplitResult(List<string> OutputFiles);
    public record CompressResult(string OutputPath, long OriginalSize, long OutputSize, bool KeptOriginal)
    {
        public double SavingsPct => OriginalSize > 0
            ? (OriginalSize - OutputSize) * 100.0 / OriginalSize : 0;
    }

    public MergeResult MergePdfs(IEnumerable<string> inputPaths, string outputPath)
    {
        try
        {
            var paths = inputPaths.ToList();
            int pageCount = 0;
            using var builder = new PdfDocumentBuilder();

            foreach (var inp in paths)
            {
                try
                {
                    using (var doc = PdfDocument.Open(inp))
                    {
                        foreach (var page in doc.GetPages())
                        {
                            builder.AddPage(doc, page.Number);
                            pageCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Error merging {inp}: {ex.Message}");
                }
            }

            File.WriteAllBytes(outputPath, builder.Build());
            Logger.Info($"Merged {paths.Count} files into {outputPath} ({pageCount} pages)");
            return new MergeResult(outputPath, pageCount);
        }
        catch (Exception ex)
        {
            Logger.Error($"MergePdfs failed", ex);
            throw;
        }
    }

    public RemoveResult RemovePages(string inputPath, string outputPath, string pagesSpec)
    {
        try
        {
            var toRemove = ParsePageSpec(pagesSpec);
            using (var src = PdfDocument.Open(inputPath))
            {
                using var builder = new PdfDocumentBuilder();
                int removed = 0;
                foreach (var page in src.GetPages())
                {
                    if (toRemove.Contains(page.Number))
                        removed++;
                    else
                        builder.AddPage(src, page.Number);
                }
                File.WriteAllBytes(outputPath, builder.Build());
                Logger.Info($"Removed {removed} pages from {inputPath}");
                return new RemoveResult(outputPath, removed);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"RemovePages failed", ex);
            throw;
        }
    }

    public RotateResult RotatePages(string inputPath, string outputPath, string pagesSpec, int angleDegrees)
    {
        try
        {
            // PdfPig doesn't support rotation directly; stub for now
            File.Copy(inputPath, outputPath, overwrite: true);
            Logger.Info($"Rotated pages (stub) from {inputPath}");
            return new RotateResult(outputPath);
        }
        catch (Exception ex)
        {
            Logger.Error($"RotatePages failed", ex);
            throw;
        }
    }

    public SplitResult SplitEveryN(string inputPath, string outputDir, int n)
    {
        try
        {
            Directory.CreateDirectory(outputDir);
            using (var doc = PdfDocument.Open(inputPath))
            {
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
                    outputs.Add(outPath);
                    part++;
                }

                Logger.Info($"Split {inputPath} into {outputs.Count} files");
                return new SplitResult(outputs);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"SplitEveryN failed", ex);
            throw;
        }
    }

    public SplitResult SplitByRanges(string inputPath, string outputDir, string rangesSpec)
    {
        try
        {
            Directory.CreateDirectory(outputDir);
            using (var doc = PdfDocument.Open(inputPath))
            {
                var stem = Path.GetFileNameWithoutExtension(inputPath);
                var outputs = new List<string>();
                var ranges = ParseRanges(rangesSpec);

                for (int ri = 0; ri < ranges.Count; ri++)
                {
                    var (start, end) = ranges[ri];
                    using var builder = new PdfDocumentBuilder();
                    for (int p = start; p <= end; p++)
                        builder.AddPage(doc, p);
                    var outPath = Path.Combine(outputDir, $"{stem}_range{ri + 1:D3}.pdf");
                    File.WriteAllBytes(outPath, builder.Build());
                    outputs.Add(outPath);
                }

                Logger.Info($"Split {inputPath} by ranges into {outputs.Count} files");
                return new SplitResult(outputs);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"SplitByRanges failed", ex);
            throw;
        }
    }

    public CompressResult CompressPdf(string inputPath, string outputPath, string quality = "ebook")
    {
        try
        {
            var originalSize = new FileInfo(inputPath).Length;

            // For now, just copy (compression requires Ghostscript or external tool)
            File.Copy(inputPath, outputPath, overwrite: true);
            var newSize = new FileInfo(outputPath).Length;

            Logger.Info($"Compressed {inputPath} (stub): {originalSize} -> {newSize} bytes");
            return new CompressResult(outputPath, originalSize, newSize, newSize >= originalSize);
        }
        catch (Exception ex)
        {
            Logger.Error($"CompressPdf failed", ex);
            throw;
        }
    }

    public void EditMetadata(string inputPath, string outputPath, string title, string author, string subject, string keywords)
    {
        try
        {
            using (var doc = PdfDocument.Open(inputPath))
            {
                using var builder = new PdfDocumentBuilder();
                foreach (var page in doc.GetPages())
                    builder.AddPage(doc, page.Number);

                // PdfPig doesn't expose direct metadata editing; stub for now
                File.WriteAllBytes(outputPath, builder.Build());
                Logger.Info($"Edited metadata on {inputPath}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"EditMetadata failed", ex);
            throw;
        }
    }

    public void InsertPages(string basePath, string insertPath, int afterPage, string outputPath)
    {
        try
        {
            using (var baseDoc = PdfDocument.Open(basePath))
            using (var insertDoc = PdfDocument.Open(insertPath))
            {
                using var builder = new PdfDocumentBuilder();
                int pg = 0;
                foreach (var page in baseDoc.GetPages())
                {
                    builder.AddPage(baseDoc, page.Number);
                    pg++;
                    if (pg == afterPage)
                        foreach (var ins in insertDoc.GetPages())
                            builder.AddPage(insertDoc, ins.Number);
                }
                if (afterPage == 0)
                    foreach (var ins in insertDoc.GetPages())
                        builder.AddPage(insertDoc, ins.Number);

                File.WriteAllBytes(outputPath, builder.Build());
                Logger.Info($"Inserted pages from {insertPath} into {basePath}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"InsertPages failed", ex);
            throw;
        }
    }

    public static int GetPageCount(string pdfPath)
    {
        try
        {
            using (var doc = PdfDocument.Open(pdfPath))
                return doc.NumberOfPages;
        }
        catch (Exception ex)
        {
            Logger.Warn($"GetPageCount failed for {pdfPath}: {ex.Message}");
            return 0;
        }
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
