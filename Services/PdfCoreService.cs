using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using PdfSharpCore.Pdf.Security;

using PdfPigDoc = UglyToad.PdfPig.PdfDocument;
using PdfSharpDoc = PdfSharpCore.Pdf.PdfDocument;

namespace PaperlessDesktop.Services;

public class PdfCoreService
{
    public class SplitResult   { public List<string> OutputFiles { get; set; } = new(); }
    public class RemoveResult  { public string OutputPath { get; set; } = ""; public int RemovedCount { get; set; } }
    public class CompressResult
    {
        public long   OriginalSize { get; set; }
        public long   OutputSize   { get; set; }
        public double SavingsPct   => OriginalSize > 0 ? (1.0 - (double)OutputSize / OriginalSize) * 100 : 0;
        public bool   KeptOriginal { get; set; }
    }

    public static int GetPageCount(string filePath)
    {
        using var doc = PdfPigDoc.Open(filePath);
        return doc.NumberOfPages;
    }

    public SplitResult SplitEveryN(string inputPath, string outputDir, int n)
    {
        var result = new SplitResult();
        Directory.CreateDirectory(outputDir);
        using var doc = PdfPigDoc.Open(inputPath);
        int total = doc.NumberOfPages;
        int idx = 1;
        for (int i = 1; i <= total; i += n)
        {
            var b = new PdfDocumentBuilder();
            int end = Math.Min(i + n - 1, total);
            for (int p = i; p <= end; p++) b.AddPage(doc, p);
            string outPath = Path.Combine(outputDir, $"split_{idx}.pdf");
            File.WriteAllBytes(outPath, b.Build());
            result.OutputFiles.Add(outPath);
            idx++;
        }
        return result;
    }

    public SplitResult SplitByRanges(string inputPath, string outputDir, string ranges)
    {
        var result = new SplitResult();
        Directory.CreateDirectory(outputDir);
        using var doc = PdfPigDoc.Open(inputPath);
        int idx = 1;
        foreach (var (start, end) in ParseRanges(ranges))
        {
            var b = new PdfDocumentBuilder();
            for (int p = start; p <= end; p++) b.AddPage(doc, p);
            string outPath = Path.Combine(outputDir, $"range_{idx}.pdf");
            File.WriteAllBytes(outPath, b.Build());
            result.OutputFiles.Add(outPath);
            idx++;
        }
        return result;
    }

    public string MergePdfs(IEnumerable<string> inputFiles, string outputPath)
    {
        using var output = new PdfSharpDoc();
        foreach (var file in inputFiles)
        {
            using var src = PdfReader.Open(file, PdfDocumentOpenMode.Import);
            for (int i = 0; i < src.PageCount; i++)
                output.AddPage(src.Pages[i]);
        }
        output.Save(outputPath);
        return outputPath;
    }

    public RemoveResult RemovePages(string inputPath, string outputPath, string pagesSpec)
        => RemovePages(inputPath, outputPath, ParsePageSpec(inputPath, pagesSpec));

    public RemoveResult RemovePages(string inputPath, string outputPath, List<int> pagesToRemove)
    {
        using var doc = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);
        int total = doc.PageCount;
        var sorted = pagesToRemove.Where(p => p >= 1 && p <= total).Distinct().OrderByDescending(p => p).ToList();
        foreach (var p in sorted) doc.Pages.RemoveAt(p - 1);
        doc.Save(outputPath);
        return new RemoveResult { OutputPath = outputPath, RemovedCount = sorted.Count };
    }

    public string RotatePages(string inputPath, string outputPath, string pagesSpec, int angle)
    {
        using var doc = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);
        int total = doc.PageCount;
        var pages = pagesSpec.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)
            ? Enumerable.Range(1, total).ToList() : ParsePageSpec(inputPath, pagesSpec);
        foreach (var p in pages.Where(p => p >= 1 && p <= total))
            doc.Pages[p - 1].Rotate = (doc.Pages[p - 1].Rotate + angle) % 360;
        doc.Save(outputPath);
        return outputPath;
    }

    public string InsertPages(string inputPath, string sourcePath, int afterPage, string outputPath)
    {
        using var dest   = PdfReader.Open(inputPath,  PdfDocumentOpenMode.Import);
        using var src    = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
        using var merged = new PdfSharpDoc();
        int insertAt = Math.Min(afterPage, dest.PageCount);
        for (int i = 0; i < insertAt;           i++) merged.AddPage(dest.Pages[i]);
        for (int i = 0; i < src.PageCount;      i++) merged.AddPage(src.Pages[i]);
        for (int i = insertAt; i < dest.PageCount; i++) merged.AddPage(dest.Pages[i]);
        merged.Save(outputPath);
        return outputPath;
    }

    public string PasswordProtect(string inputPath, string outputPath, string userPwd, string ownerPwd)
    {
        using var doc = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);
        var sec = doc.SecuritySettings;
        sec.UserPassword  = userPwd;
        sec.OwnerPassword = ownerPwd;
        sec.PermitPrint          = true;
        sec.PermitModifyDocument = false;
        sec.PermitExtractContent = false;
        sec.PermitAnnotations    = false;
        doc.Save(outputPath);
        return outputPath;
    }

    public string UnlockPdf(string inputPath, string outputPath, string password)
    {
        using var doc = PdfReader.Open(inputPath, password, PdfDocumentOpenMode.Modify);
        doc.SecuritySettings.UserPassword  = "";
        doc.SecuritySettings.OwnerPassword = "";
        doc.Save(outputPath);
        return outputPath;
    }

    public string EditMetadata(string inputPath, string outputPath,
        string title, string author, string subject, string keywords)
    {
        using var doc = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);
        if (!string.IsNullOrWhiteSpace(title))    doc.Info.Title    = title;
        if (!string.IsNullOrWhiteSpace(author))   doc.Info.Author   = author;
        if (!string.IsNullOrWhiteSpace(subject))  doc.Info.Subject  = subject;
        if (!string.IsNullOrWhiteSpace(keywords)) doc.Info.Keywords = keywords;
        doc.Save(outputPath);
        return outputPath;
    }

    public CompressResult CompressPdf(string inputPath, string outputPath, string? quality = "ebook")
    {
        long origSize = new FileInfo(inputPath).Length;
        using var doc = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);
        doc.Options.CompressContentStreams = true;
        doc.Options.NoCompression         = false;
        doc.Save(outputPath);
        long outSize = new FileInfo(outputPath).Length;
        if (outSize >= origSize)
        {
            File.Copy(inputPath, outputPath, true);
            return new CompressResult { OriginalSize = origSize, OutputSize = origSize, KeptOriginal = true };
        }
        return new CompressResult { OriginalSize = origSize, OutputSize = outSize };
    }

    public string ExtractText(string filePath)
    {
        using var doc = PdfPigDoc.Open(filePath);
        var sb = new StringBuilder();
        foreach (var page in doc.GetPages()) sb.AppendLine(page.Text);
        return sb.ToString();
    }

    private List<int> ParsePageSpec(string inputPath, string spec)
    {
        if (spec.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
            return Enumerable.Range(1, GetPageCount(inputPath)).ToList();
        return ParseRanges(spec).SelectMany(r => Enumerable.Range(r.start, r.end - r.start + 1)).Distinct().ToList();
    }

    private static List<(int start, int end)> ParseRanges(string input)
    {
        var result = new List<(int, int)>();
        foreach (var part in input.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Contains('-'))
            {
                var range = part.Split('-');
                result.Add((int.Parse(range[0].Trim()), int.Parse(range[1].Trim())));
            }
            else { int p = int.Parse(part.Trim()); result.Add((p, p)); }
        }
        return result;
    }
}