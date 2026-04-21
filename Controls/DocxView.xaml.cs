using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.UI.Text;
using PaperlessDesktop.Services;
using PaperlessDesktop.ViewModels;
using System.Text;

namespace PaperlessDesktop.Controls;

public sealed partial class DocxView : UserControl
{
    private MainViewModel _vm = null!;
    private Window _win = null!;
    private string? _currentFilePath;
    private bool _isUpdatingToolbar;

    public DocxView() => InitializeComponent();

    public void Initialize(MainViewModel vm, Window win)
    {
        _vm = vm;
        _win = win;
    }

    // ── Load / Unload ─────────────────────────────────────────────────────────

    public void LoadFile(string path)
    {
        _currentFilePath = path;
        FileNameBlock.Text = Path.GetFileName(path);
        Editor.IsReadOnly = false;
        SaveDocxBtn.IsEnabled = true;
        BtnSave.IsEnabled = true;
        BtnSaveAs.IsEnabled = true;
        BtnFindReplace.IsEnabled = true;
        BtnMergeDocx.IsEnabled = _vm.Files.Count(f => f.IsSelected &&
            ViewHelpers.IsDocx(f.FilePath)) >= 2;

        try
        {
            var rtf = ConvertDocxToRtf(path);
            Editor.Document.SetText(TextSetOptions.FormatRtf, rtf);
            StatusText.Text = $"Loaded {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error loading: {ex.Message}";
        }
    }

    public void Clear()
    {
        _currentFilePath = null;
        FileNameBlock.Text = "";
        Editor.IsReadOnly = true;
        Editor.Document.SetText(TextSetOptions.None, "");
        StatusText.Text = "";
        SaveDocxBtn.IsEnabled = false;
        BtnSave.IsEnabled = false;
        BtnSaveAs.IsEnabled = false;
        BtnFindReplace.IsEnabled = false;
        BtnMergeDocx.IsEnabled = false;
    }

    public void RefreshButtons()
    {
        var selDocx = _vm.Files.Where(f => f.IsSelected && ViewHelpers.IsDocx(f.FilePath)).ToList();
        BtnMergeDocx.IsEnabled = selDocx.Count >= 2;
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    private void SaveDocx_Click(object s, RoutedEventArgs e)
    {
        if (_currentFilePath is null) return;
        try
        {
            Editor.Document.GetText(TextGetOptions.FormatRtf, out string rtf);
            ConvertRtfToDocx(rtf, _currentFilePath);
            StatusText.Text = $"Saved {Path.GetFileName(_currentFilePath)}";
            _vm.StatusText = $"Saved {Path.GetFileName(_currentFilePath)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error saving: {ex.Message}";
        }
    }

    private async void SaveDocxAs_Click(object s, RoutedEventArgs e)
    {
        if (_currentFilePath is null) return;
        var out_ = await ViewHelpers.SaveFile(_win,
            Path.GetFileName(_currentFilePath), ".docx");
        if (out_ is null) return;
        try
        {
            Editor.Document.GetText(TextGetOptions.FormatRtf, out string rtf);
            ConvertRtfToDocx(rtf, out_);
            StatusText.Text = $"Saved as {Path.GetFileName(out_)}";
            _vm.StatusText = $"Saved {Path.GetFileName(out_)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error saving: {ex.Message}";
        }
    }

    // ── Formatting toolbar ────────────────────────────────────────────────────

    private void BoldBtn_Click(object s, RoutedEventArgs e)
    {
        if (_isUpdatingToolbar) return;
        Editor.Document.Selection.CharacterFormat.Bold =
            BoldBtn.IsChecked == true ? FormatEffect.On : FormatEffect.Off;
    }

    private void ItalicBtn_Click(object s, RoutedEventArgs e)
    {
        if (_isUpdatingToolbar) return;
        Editor.Document.Selection.CharacterFormat.Italic =
            ItalicBtn.IsChecked == true ? FormatEffect.On : FormatEffect.Off;
    }

    private void UnderlineBtn_Click(object s, RoutedEventArgs e)
    {
        if (_isUpdatingToolbar) return;
        Editor.Document.Selection.CharacterFormat.Underline =
            UnderlineBtn.IsChecked == true ? UnderlineType.Single : UnderlineType.None;
    }

    private void FontCombo_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (_isUpdatingToolbar || FontCombo.SelectedItem is not string font) return;
        Editor.Document.Selection.CharacterFormat.Name = font;
    }

    private void FontSizeCombo_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (_isUpdatingToolbar || FontSizeCombo.SelectedItem is not string sizeStr) return;
        if (float.TryParse(sizeStr, out float size))
            Editor.Document.Selection.CharacterFormat.Size = size;
    }

    private void AlignLeftBtn_Click(object s, RoutedEventArgs e)
    {
        if (_isUpdatingToolbar) return;
        Editor.Document.Selection.ParagraphFormat.Alignment = ParagraphAlignment.Left;
        UpdateAlignButtons(ParagraphAlignment.Left);
    }

    private void AlignCenterBtn_Click(object s, RoutedEventArgs e)
    {
        if (_isUpdatingToolbar) return;
        Editor.Document.Selection.ParagraphFormat.Alignment = ParagraphAlignment.Center;
        UpdateAlignButtons(ParagraphAlignment.Center);
    }

    private void AlignRightBtn_Click(object s, RoutedEventArgs e)
    {
        if (_isUpdatingToolbar) return;
        Editor.Document.Selection.ParagraphFormat.Alignment = ParagraphAlignment.Right;
        UpdateAlignButtons(ParagraphAlignment.Right);
    }

    private void AlignJustifyBtn_Click(object s, RoutedEventArgs e)
    {
        if (_isUpdatingToolbar) return;
        Editor.Document.Selection.ParagraphFormat.Alignment = ParagraphAlignment.Justify;
        UpdateAlignButtons(ParagraphAlignment.Justify);
    }

    private void BulletsBtn_Click(object s, RoutedEventArgs e)
    {
        if (_isUpdatingToolbar) return;
        Editor.Document.Selection.ParagraphFormat.ListType =
            BulletsBtn.IsChecked == true ? MarkerType.Bullet : MarkerType.None;
    }

    private async void ColorBtn_Click(object s, RoutedEventArgs e)
    {
        var picker = new ColorPicker { Color = Windows.UI.Color.FromArgb(255, 0, 0, 0) };
        var dlg = new ContentDialog
        {
            Title = "Text Color",
            Content = picker,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            Editor.Document.Selection.CharacterFormat.ForegroundColor = picker.Color;
    }

    private void Editor_SelectionChanged(object s, RoutedEventArgs e)
    {
        _isUpdatingToolbar = true;
        var fmt = Editor.Document.Selection.CharacterFormat;
        var paraFmt = Editor.Document.Selection.ParagraphFormat;
        BoldBtn.IsChecked = fmt.Bold == FormatEffect.On;
        ItalicBtn.IsChecked = fmt.Italic == FormatEffect.On;
        UnderlineBtn.IsChecked = fmt.Underline != UnderlineType.None;
        BulletsBtn.IsChecked = paraFmt.ListType != MarkerType.None;
        UpdateAlignButtons(paraFmt.Alignment);
        if (!string.IsNullOrEmpty(fmt.Name)) FontCombo.SelectedItem = fmt.Name;
        if (fmt.Size > 0) FontSizeCombo.SelectedItem = ((int)fmt.Size).ToString();
        _isUpdatingToolbar = false;
    }

    private void UpdateAlignButtons(ParagraphAlignment align)
    {
        AlignLeftBtn.IsChecked = align == ParagraphAlignment.Left;
        AlignCenterBtn.IsChecked = align == ParagraphAlignment.Center;
        AlignRightBtn.IsChecked = align == ParagraphAlignment.Right;
        AlignJustifyBtn.IsChecked = align == ParagraphAlignment.Justify;
    }

    // ── DOCX Tool actions ─────────────────────────────────────────────────────

    private async void FindReplace_Click(object s, RoutedEventArgs e)
    {
        if (!ViewHelpers.RequirePro(Content.XamlRoot, App.Services.GetRequiredService<LicenseService>(), "Find & Replace")) return;
        if (_currentFilePath is null) { await ViewHelpers.Info(Content.XamlRoot, "Find & Replace", "No document loaded."); return; }

        var find = await ViewHelpers.Ask(Content.XamlRoot, "Find & Replace", "Text to find:");
        if (find is null) return;
        var replace = await ViewHelpers.Ask(Content.XamlRoot, "Find & Replace", "Replace with:", "");
        if (replace is null) return;

        var out_ = await ViewHelpers.SaveFile(_win,
            $"{Path.GetFileNameWithoutExtension(_currentFilePath)}_replaced.docx", ".docx");
        if (out_ is null) return;

        int count = 0;
        await ViewHelpers.Run(Content.XamlRoot, "Find & Replace…", (_, _) =>
        {
            File.Copy(_currentFilePath, out_, true);
            using var doc = WordprocessingDocument.Open(out_, true);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body is not null)
            {
                foreach (var text in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>())
                {
                    if (text.Text.Contains(find))
                    {
                        count += CountOccurrences(text.Text, find);
                        text.Text = text.Text.Replace(find, replace);
                    }
                }
            }
            return Task.CompletedTask;
        });
        await ViewHelpers.Info(Content.XamlRoot, "Find & Replace", $"✓  Replaced {count} occurrence(s).\n{out_}");
    }

    private async void MergeDocx_Click(object s, RoutedEventArgs e)
    {
        if (!ViewHelpers.RequirePro(Content.XamlRoot, App.Services.GetRequiredService<LicenseService>(), "Merge DOCX")) return;
        var sel = _vm.Files.Where(f => f.IsSelected && ViewHelpers.IsDocx(f.FilePath)).ToList();
        if (sel.Count < 2) { await ViewHelpers.Info(Content.XamlRoot, "Merge DOCX", "Select at least 2 DOCX files."); return; }

        var out_ = await ViewHelpers.SaveFile(_win, "merged.docx", ".docx");
        if (out_ is null) return;

        await ViewHelpers.Run(Content.XamlRoot, "Merging DOCX…", (_, _) =>
        {
            File.Copy(sel[0].FilePath, out_, true);
            using var doc = WordprocessingDocument.Open(out_, true);
            var body = doc.MainDocumentPart!.Document!.Body!;

            for (int i = 1; i < sel.Count; i++)
            {
                body.AppendChild(new Paragraph(
                    new Run(new Break { Type = BreakValues.Page })));

                using var srcDoc = WordprocessingDocument.Open(sel[i].FilePath, false);
                var srcBody = srcDoc.MainDocumentPart?.Document?.Body;
                if (srcBody is not null)
                {
                    foreach (var element in srcBody.ChildElements)
                    {
                        if (element is SectionProperties) continue;
                        body.AppendChild(element.CloneNode(true));
                    }
                }
            }
            return Task.CompletedTask;
        });
        await ViewHelpers.Info(Content.XamlRoot, "Merge DOCX", $"✓  {sel.Count} documents merged.\n{out_}");
    }

    // ── DOCX ↔ RTF Conversion (same logic as DocxEditorDialog) ───────────────

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
                var text = run.InnerText
                    .Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}");
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
        using var doc = WordprocessingDocument.Create(docxPath,
            DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());
        var body = mainPart.Document.Body!;

        var lines = rtf.Split(new[] { @"\par" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            var text = StripRtfTags(line).Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;
            var para = new Paragraph();
            var run = new Run(new DocumentFormat.OpenXml.Wordprocessing.Text(text));
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

    private static int CountOccurrences(string text, string find)
    {
        int count = 0, idx = 0;
        while ((idx = text.IndexOf(find, idx, StringComparison.Ordinal)) >= 0)
        { count++; idx += find.Length; }
        return count;
    }
}
