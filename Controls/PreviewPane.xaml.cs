namespace PaperlessDesktop.Controls;

public sealed partial class PreviewPane : UserControl
{
    private Windows.Data.Pdf.PdfDocument? _pdf;
    private uint _currentPage = 0;
    private uint _totalPages = 0;

    public PreviewPane() => InitializeComponent();

    public async void LoadPdf(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            _pdf = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
            _totalPages = _pdf.PageCount;
            _currentPage = 0;
            FileNameBlock.Text = Path.GetFileName(path);
            EmptyState.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            PageImage.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            PreviewText.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            PrevPageBtn.IsEnabled = false;
            NextPageBtn.IsEnabled = _totalPages > 1;
            SearchBox.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            await RenderPageAsync(_currentPage);
        }
        catch { }
    }

    public async void LoadImage(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(stream);

            _pdf = null;
            FileNameBlock.Text = Path.GetFileName(path);
            EmptyState.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            PageImage.Source = bmp;
            PageImage.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            PreviewText.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            PageLabel.Text = $"{bmp.PixelWidth} × {bmp.PixelHeight}";
            PrevPageBtn.IsEnabled = false;
            NextPageBtn.IsEnabled = false;
            SearchBox.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }
        catch { }
    }

    public async void LoadDocx(string path)
    {
        try
        {
            var text = ExtractDocxText(path);
            ShowTextPreview(Path.GetFileName(path), text);
        }
        catch { }
    }

    public async void LoadCsvXlsx(string path)
    {
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            string[][] rows;

            if (ext == ".csv")
            {
                var lines = await File.ReadAllLinesAsync(path);
                rows = lines.Select(l => ParseCsvLine(l)).ToArray();
            }
            else
            {
                rows = ReadXlsx(path);
            }

            var sb = new System.Text.StringBuilder();
            int previewRows = Math.Min(rows.Length, 100);
            for (int r = 0; r < previewRows; r++)
            {
                sb.AppendLine(string.Join(" │ ", rows[r].Select(c => c.Length > 40 ? c[..37] + "..." : c)));
            }
            if (rows.Length > 100) sb.AppendLine($"... and {rows.Length - 100} more row(s)");

            ShowTextPreview(Path.GetFileName(path), sb.ToString());
        }
        catch { }
    }

    private void ShowTextPreview(string fileName, string text)
    {
        _pdf = null;
        FileNameBlock.Text = fileName;
        EmptyState.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        PageImage.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        PreviewText.Text = text;
        PreviewText.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        PageLabel.Text = $"{text.Length} chars";
        PrevPageBtn.IsEnabled = false;
        NextPageBtn.IsEnabled = false;
        SearchBox.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private static string ExtractDocxText(string path)
    {
        using var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(path, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return "(empty document)";

        var sb = new System.Text.StringBuilder();
        foreach (var para in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
        {
            var text = para.InnerText;
            if (!string.IsNullOrWhiteSpace(text))
                sb.AppendLine(text);
        }
        return sb.ToString();
    }

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuote = false;
        var field = new System.Text.StringBuilder();
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuote)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') inQuote = false;
                else field.Append(c);
            }
            else
            {
                if (c == '"') inQuote = true;
                else if (c == ',') { result.Add(field.ToString()); field.Clear(); }
                else field.Append(c);
            }
        }
        result.Add(field.ToString());
        return result.ToArray();
    }

    private static string[][] ReadXlsx(string path)
    {
        using var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(path, false);
        var wbPart = doc.WorkbookPart;
        var sheet = wbPart?.WorksheetParts?.FirstOrDefault();
        if (sheet is null) return Array.Empty<string[]>();

        var sharedStrings = wbPart!.SharedStringTablePart?.SharedStringTable
            ?.Elements<DocumentFormat.OpenXml.Spreadsheet.SharedStringItem>()
            ?.Select(s => s.InnerText).ToArray() ?? Array.Empty<string>();

        var rows = sheet.Worksheet.Descendants<DocumentFormat.OpenXml.Spreadsheet.Row>();
        return rows.Select(row =>
        {
            return row.Elements<DocumentFormat.OpenXml.Spreadsheet.Cell>().Select(cell =>
            {
                var val = cell.CellValue?.InnerText ?? "";
                if (cell.DataType?.Value == DocumentFormat.OpenXml.Spreadsheet.CellValues.SharedString
                    && int.TryParse(val, out int idx) && idx < sharedStrings.Length)
                    return sharedStrings[idx];
                return val;
            }).ToArray();
        }).ToArray();
    }

    private async Task RenderPageAsync(uint pageIndex)
    {
        if (_pdf is null) return;
        using var page = _pdf.GetPage(pageIndex);
        var stream = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(stream);
        var bmp = new BitmapImage();
        await bmp.SetSourceAsync(stream);
        PageImage.Source = bmp;
        PageLabel.Text = $"{pageIndex + 1} / {_totalPages}";
    }

    private async void PrevPageBtn_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_currentPage > 0)
        {
            _currentPage--;
            PrevPageBtn.IsEnabled = _currentPage > 0;
            NextPageBtn.IsEnabled = true;
            await RenderPageAsync(_currentPage);
        }
    }

    private async void NextPageBtn_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_currentPage < _totalPages - 1)
        {
            _currentPage++;
            NextPageBtn.IsEnabled = _currentPage < _totalPages - 1;
            PrevPageBtn.IsEnabled = true;
            await RenderPageAsync(_currentPage);
        }
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox s, AutoSuggestBoxQuerySubmittedEventArgs e)
    {
        // TODO: implement text search using Windows.Data.Pdf.PdfPage.GetTextRanges()
        // See roadmap Step 8
    }

    public void Clear()
    {
        _pdf = null;
        _currentPage = 0;
        _totalPages = 0;
        PageImage.Source = null;
        FileNameBlock.Text = "";
        PageLabel.Text = "";
        EmptyState.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        PrevPageBtn.IsEnabled = false;
        NextPageBtn.IsEnabled = false;
    }
}