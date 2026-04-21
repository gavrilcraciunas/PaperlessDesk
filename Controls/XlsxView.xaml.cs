using PaperlessDesktop.ViewModels;

namespace PaperlessDesktop.Controls;

public sealed partial class XlsxView : UserControl
{
    private MainViewModel _vm = null!;
    private Window _win = null!;
    private string? _currentFilePath;
    private string[][]? _rows;

    public XlsxView() => InitializeComponent();

    public void Initialize(MainViewModel vm, Window win)
    {
        _vm = vm;
        _win = win;
    }

    // ── Load / Unload ─────────────────────────────────────────────────────────

    public async void LoadFile(string path)
    {
        _currentFilePath = path;
        FileNameBlock.Text = Path.GetFileName(path);
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".csv")
            {
                var lines = await File.ReadAllLinesAsync(path);
                _rows = lines.Select(l => ParseCsvLine(l)).ToArray();
            }
            else
            {
                _rows = ReadXlsx(path);
            }

            if (_rows.Length == 0)
            {
                TableText.Text = "(empty file)";
                StatsBlock.Text = "";
            }
            else
            {
                int maxCols = _rows.Max(r => r.Length);
                StatsBlock.Text = $"{_rows.Length} rows × {maxCols} cols";
                int previewRows = Math.Min(_rows.Length, 200);
                var sb = new StringBuilder();
                for (int r = 0; r < previewRows; r++)
                    sb.AppendLine(string.Join(" │ ",
                        _rows[r].Select(c => c.Length > 40 ? c[..37] + "..." : c)));
                if (_rows.Length > 200) sb.AppendLine($"… and {_rows.Length - 200} more row(s)");
                TableText.Text = sb.ToString();
            }
        }
        catch (Exception ex)
        {
            TableText.Text = $"Error loading file: {ex.Message}";
        }

        BtnExportCsv.IsEnabled = _currentFilePath is not null;
        BtnExportXlsx.IsEnabled = _currentFilePath is not null;
    }

    public void Clear()
    {
        _currentFilePath = null;
        _rows = null;
        FileNameBlock.Text = "";
        StatsBlock.Text = "";
        TableText.Text = "";
        BtnExportCsv.IsEnabled = false;
        BtnExportXlsx.IsEnabled = false;
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    private async void ExportCsv_Click(object s, RoutedEventArgs e)
    {
        if (_rows is null || _currentFilePath is null) return;
        var out_ = await ViewHelpers.SaveFile(_win,
            Path.GetFileNameWithoutExtension(_currentFilePath) + ".csv", ".csv");
        if (out_ is null) return;
        var csvLines = _rows.Select(r =>
            string.Join(",", r.Select(c =>
                c.Contains(',') || c.Contains('"')
                    ? $"\"{c.Replace("\"", "\"\"")}\"" : c)));
        await File.WriteAllLinesAsync(out_, csvLines);
        await ViewHelpers.Info(Content.XamlRoot, "Export", $"✓  CSV saved.\n{out_}");
    }

    private async void ExportXlsx_Click(object s, RoutedEventArgs e)
    {
        if (_rows is null || _currentFilePath is null) return;
        var out_ = await ViewHelpers.SaveFile(_win,
            Path.GetFileNameWithoutExtension(_currentFilePath) + ".xlsx", ".xlsx");
        if (out_ is null) return;
        WriteXlsx(out_, _rows);
        await ViewHelpers.Info(Content.XamlRoot, "Export", $"✓  XLSX saved.\n{out_}");
    }

    // ── CSV / XLSX helpers ────────────────────────────────────────────────────

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuote = false;
        var field = new StringBuilder();
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
        return rows.Select(row => row
            .Elements<DocumentFormat.OpenXml.Spreadsheet.Cell>()
            .Select(cell =>
            {
                var val = cell.CellValue?.InnerText ?? "";
                if (cell.DataType?.Value == DocumentFormat.OpenXml.Spreadsheet.CellValues.SharedString
                    && int.TryParse(val, out int idx) && idx < sharedStrings.Length)
                    return sharedStrings[idx];
                return val;
            }).ToArray()).ToArray();
    }

    private static void WriteXlsx(string path, string[][] rows)
    {
        using var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Create(
            path, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook);
        var wbPart = doc.AddWorkbookPart();
        wbPart.Workbook = new DocumentFormat.OpenXml.Spreadsheet.Workbook();
        var wsPart = wbPart.AddNewPart<DocumentFormat.OpenXml.Packaging.WorksheetPart>();
        var sheetData = new DocumentFormat.OpenXml.Spreadsheet.SheetData();
        wsPart.Worksheet = new DocumentFormat.OpenXml.Spreadsheet.Worksheet(sheetData);

        foreach (var rowData in rows)
        {
            var row = new DocumentFormat.OpenXml.Spreadsheet.Row();
            foreach (var cellText in rowData)
                row.Append(new DocumentFormat.OpenXml.Spreadsheet.Cell
                {
                    DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.String,
                    CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(cellText)
                });
            sheetData.Append(row);
        }

        var sheets = wbPart.Workbook.AppendChild(new DocumentFormat.OpenXml.Spreadsheet.Sheets());
        sheets.Append(new DocumentFormat.OpenXml.Spreadsheet.Sheet
        {
            Id = wbPart.GetIdOfPart(wsPart),
            SheetId = 1,
            Name = "Sheet1"
        });
    }
}
