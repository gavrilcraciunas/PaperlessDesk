using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Text;
using Windows.Storage.Streams;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text;

namespace PaperlessDesktop.Dialogs;

public sealed partial class DocxEditorDialog : ContentDialog
{
    private string _filePath = "";
    private bool _isUpdatingToolbar = false;

    public DocxEditorDialog(XamlRoot xamlRoot, string filePath)
    {
        InitializeComponent();
        XamlRoot = xamlRoot;
        _filePath = filePath;
        FileNameBlock.Text = Path.GetFileName(filePath);
        FontSizeCombo.SelectedItem = "12";
        FontCombo.SelectedItem = "Calibri";
        LoadDocx();
    }

    private void LoadDocx()
    {
        try
        {
            // Convert DOCX to RTF for RichEditBox
            var rtf = ConvertDocxToRtf(_filePath);
            Editor.Document.SetText(TextSetOptions.FormatRtf, rtf);
            UpdateStatus($"Loaded {Path.GetFileName(_filePath)}");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Error loading: {ex.Message}");
        }
    }

    public string GetRtfContent()
    {
        Editor.Document.GetText(TextGetOptions.FormatRtf, out string rtf);
        return rtf;
    }

    public void SaveToDocx(string outputPath)
    {
        try
        {
            Editor.Document.GetText(TextGetOptions.FormatRtf, out string rtf);
            ConvertRtfToDocx(rtf, outputPath);
            UpdateStatus($"Saved to {Path.GetFileName(outputPath)}");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Error saving: {ex.Message}");
        }
    }

    // ── Formatting handlers ──────────────────────────────────────────────────

    private void BoldBtn_Click(object sender, RoutedEventArgs e)
    {
        var selection = Editor.Document.Selection;
        if (_isUpdatingToolbar) return;

        selection.CharacterFormat.Bold = BoldBtn.IsChecked == true ? FormatEffect.On : FormatEffect.Off;
    }

    private void ItalicBtn_Click(object sender, RoutedEventArgs e)
    {
        var selection = Editor.Document.Selection;
        if (_isUpdatingToolbar) return;

        selection.CharacterFormat.Italic = ItalicBtn.IsChecked == true ? FormatEffect.On : FormatEffect.Off;
    }

    private void UnderlineBtn_Click(object sender, RoutedEventArgs e)
    {
        var selection = Editor.Document.Selection;
        if (_isUpdatingToolbar) return;

        selection.CharacterFormat.Underline = UnderlineBtn.IsChecked == true 
            ? UnderlineType.Single : UnderlineType.None;
    }

    private void FontCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingToolbar || FontCombo.SelectedItem is not string font) return;
        Editor.Document.Selection.CharacterFormat.Name = font;
    }

    private void FontSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingToolbar || FontSizeCombo.SelectedItem is not string sizeStr) return;
        if (float.TryParse(sizeStr, out float size))
            Editor.Document.Selection.CharacterFormat.Size = size;
    }

    private void AlignLeftBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingToolbar) return;
        Editor.Document.Selection.ParagraphFormat.Alignment = ParagraphAlignment.Left;
        UpdateAlignmentButtons(ParagraphAlignment.Left);
    }

    private void AlignCenterBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingToolbar) return;
        Editor.Document.Selection.ParagraphFormat.Alignment = ParagraphAlignment.Center;
        UpdateAlignmentButtons(ParagraphAlignment.Center);
    }

    private void AlignRightBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingToolbar) return;
        Editor.Document.Selection.ParagraphFormat.Alignment = ParagraphAlignment.Right;
        UpdateAlignmentButtons(ParagraphAlignment.Right);
    }

    private void AlignJustifyBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingToolbar) return;
        Editor.Document.Selection.ParagraphFormat.Alignment = ParagraphAlignment.Justify;
        UpdateAlignmentButtons(ParagraphAlignment.Justify);
    }

    private void BulletsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingToolbar) return;
        var fmt = Editor.Document.Selection.ParagraphFormat;
        fmt.ListType = BulletsBtn.IsChecked == true ? MarkerType.Bullet : MarkerType.None;
    }

    private async void ColorBtn_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ColorPicker { Color = Windows.UI.Color.FromArgb(255, 0, 0, 0) };
        var dlg = new ContentDialog
        {
            Title = "Text Color",
            Content = picker,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
        {
            var color = picker.Color;
            Editor.Document.Selection.CharacterFormat.ForegroundColor = color;
        }
    }

    private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        // Update toolbar to reflect current selection formatting
        _isUpdatingToolbar = true;
        var fmt = Editor.Document.Selection.CharacterFormat;
        var paraFmt = Editor.Document.Selection.ParagraphFormat;

        BoldBtn.IsChecked = fmt.Bold == FormatEffect.On;
        ItalicBtn.IsChecked = fmt.Italic == FormatEffect.On;
        UnderlineBtn.IsChecked = fmt.Underline != UnderlineType.None;
        BulletsBtn.IsChecked = paraFmt.ListType != MarkerType.None;

        UpdateAlignmentButtons(paraFmt.Alignment);

        if (!string.IsNullOrEmpty(fmt.Name))
            FontCombo.SelectedItem = fmt.Name;
        if (fmt.Size > 0)
            FontSizeCombo.SelectedItem = ((int)fmt.Size).ToString();

        _isUpdatingToolbar = false;
    }

    private void UpdateAlignmentButtons(ParagraphAlignment align)
    {
        AlignLeftBtn.IsChecked = align == ParagraphAlignment.Left;
        AlignCenterBtn.IsChecked = align == ParagraphAlignment.Center;
        AlignRightBtn.IsChecked = align == ParagraphAlignment.Right;
        AlignJustifyBtn.IsChecked = align == ParagraphAlignment.Justify;
    }

    private void UpdateStatus(string msg)
    {
        StatusText.Text = msg;
    }

    // ── DOCX ↔ RTF Conversion ───────────────────────────────────────────────

    private static string ConvertDocxToRtf(string docxPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine(@"{\rtf1\ansi\deff0");

        using var doc = WordprocessingDocument.Open(docxPath, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return @"{\rtf1}";

        foreach (var para in body.Descendants<Paragraph>())
        {
            sb.Append(@"\par ");
            foreach (var run in para.Descendants<Run>())
            {
                var props = run.RunProperties;
                bool bold = props?.Bold?.Val?.Value == true;
                bool italic = props?.Italic?.Val?.Value == true;
                bool underline = props?.Underline is not null;

                if (bold) sb.Append(@"\b ");
                if (italic) sb.Append(@"\i ");
                if (underline) sb.Append(@"\ul ");

                var text = run.InnerText;
                // Escape RTF special chars
                text = text.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}");
                sb.Append(text);

                if (bold) sb.Append(@"\b0 ");
                if (italic) sb.Append(@"\i0 ");
                if (underline) sb.Append(@"\ulnone ");
            }
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void ConvertRtfToDocx(string rtf, string docxPath)
    {
        // Simple RTF to DOCX: extract plain text and basic formatting
        using var doc = WordprocessingDocument.Create(docxPath, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());
        var body = mainPart.Document.Body!;

        // Parse RTF (very basic - just extract text between \par markers)
        var lines = rtf.Split(new[] { @"\par" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            var text = StripRtfTags(line).Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;

            var para = new Paragraph();
            var run = new Run(new Text(text));

            // Basic formatting detection
            if (line.Contains(@"\b "))
                run.RunProperties = new RunProperties(new Bold());
            if (line.Contains(@"\i "))
            {
                run.RunProperties ??= new RunProperties();
                run.RunProperties.Append(new Italic());
            }
            if (line.Contains(@"\ul "))
            {
                run.RunProperties ??= new RunProperties();
                run.RunProperties.Append(new Underline { Val = UnderlineValues.Single });
            }

            para.Append(run);
            body.Append(para);
        }
    }

    private static string StripRtfTags(string rtf)
    {
        var sb = new StringBuilder();
        bool inTag = false;
        foreach (char c in rtf)
        {
            if (c == '\\') inTag = true;
            else if (c == ' ' && inTag) inTag = false;
            else if (!inTag && c != '{' && c != '}')
                sb.Append(c);
        }
        return sb.ToString();
    }
}
