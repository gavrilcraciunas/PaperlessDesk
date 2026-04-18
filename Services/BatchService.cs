namespace PaperlessDesktop.Services;

public record BatchItemResult(string InputPath, string OutputPath, bool Ok, string Message);

public class BatchResult
{
    public List<BatchItemResult> Results { get; } = new();
    public int Total => Results.Count;
    public int OkCount => Results.Count(r => r.Ok);
    public int FailCount => Results.Count(r => !r.Ok);
    public string Summary() => FailCount == 0
        ? $"All {OkCount} file(s) processed successfully."
        : $"{OkCount}/{Total} succeeded, {FailCount} failed.";
}

public class BatchService(PdfCoreService core, PdfConvertService convert)
{
    public Task<BatchResult> BatchCompressAsync(
        IEnumerable<string> paths, string outputDir, string quality = "ebook",
        IProgress<(int, int, string)>? prog = null, CancellationToken ct = default)
        => RunBatchAsync(paths, outputDir, "_compressed", ct, prog,
            (i, o) => { core.CompressPdf(i, o, quality); return Task.CompletedTask; });

    public Task<BatchResult> BatchOcrAsync(
        IEnumerable<string> paths, string outputDir, string lang = "ron+eng",
        IProgress<(int, int, string)>? prog = null, CancellationToken ct = default)
        => RunBatchAsync(paths, outputDir, "_ocr", ct, prog,
            (i, o) => convert.OcrPdfAsync(i, o, lang: lang, ct: ct)
                              .ContinueWith(_ => Task.CompletedTask, ct).Unwrap());

    public Task<BatchResult> BatchRemovePagesAsync(
        IEnumerable<string> paths, string outputDir, string pagesSpec,
        IProgress<(int, int, string)>? prog = null, CancellationToken ct = default)
        => RunBatchAsync(paths, outputDir, "_pages_removed", ct, prog,
            (i, o) => { core.RemovePages(i, o, pagesSpec); return Task.CompletedTask; });

    public Task<BatchResult> BatchRotateAsync(
        IEnumerable<string> paths, string outputDir, string pagesSpec, int angle,
        IProgress<(int, int, string)>? prog = null, CancellationToken ct = default)
        => RunBatchAsync(paths, outputDir, "_rotated", ct, prog,
            (i, o) => { core.RotatePages(i, o, pagesSpec, angle); return Task.CompletedTask; });

    public async Task<BatchResult> BatchPdfToImagesAsync(
        IEnumerable<string> paths, string outputDir, string format = "png",
        IProgress<(int, int, string)>? prog = null, CancellationToken ct = default)
    {
        var list = paths.ToList();
        var result = new BatchResult();
        int done = 0;
        foreach (var inp in list)
        {
            ct.ThrowIfCancellationRequested();
            var itemDir = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(inp));
            try
            {
                var imgs = await convert.PdfToImagesAsync(inp, itemDir, format, ct: ct);
                result.Results.Add(new(inp, itemDir, true, $"{imgs.Count} images"));
            }
            catch (Exception ex) { result.Results.Add(new(inp, itemDir, false, ex.Message)); }
            prog?.Report((++done, list.Count, Path.GetFileName(inp)));
        }
        return result;
    }

    public static void ExportReport(BatchResult result, string csvPath)
    {
        var lines = new List<string> { "Input,Output,Status,Message" };
        foreach (var r in result.Results)
            lines.Add($"\"{r.InputPath}\",\"{r.OutputPath}\",{(r.Ok ? "OK" : "FAILED")},\"{r.Message}\"");
        File.WriteAllLines(csvPath, lines);
    }

    private static async Task<BatchResult> RunBatchAsync(
        IEnumerable<string> paths, string outputDir, string suffix,
        CancellationToken ct, IProgress<(int, int, string)>? prog,
        Func<string, string, Task> operation)
    {
        var list = paths.ToList();
        var result = new BatchResult();
        Directory.CreateDirectory(outputDir);
        int done = 0;
        foreach (var inp in list)
        {
            ct.ThrowIfCancellationRequested();
            var outp = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(inp) + suffix + ".pdf");
            try { await operation(inp, outp); result.Results.Add(new(inp, outp, true, "OK")); }
            catch (Exception ex) { result.Results.Add(new(inp, outp, false, ex.Message)); }
            prog?.Report((++done, list.Count, Path.GetFileName(inp)));
        }
        return result;
    }
}
