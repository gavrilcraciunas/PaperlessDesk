using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using PaperlessDesktop.Services;
using PaperlessDesktop.ViewModels;
using Windows.UI;
using WinBorder = Microsoft.UI.Xaml.Controls.Border;
using WinColor = Windows.UI.Color;
using OxColor = DocumentFormat.OpenXml.Wordprocessing.Color;
using OxBorder = DocumentFormat.OpenXml.Wordprocessing.Border;

namespace PaperlessDesktop.Controls;

public sealed partial class DocxView : UserControl
{
    private MainViewModel _vm = null!;
    private Window _win = null!;
    private string? _currentFilePath;

    // Tracks the last chosen color so the swatch underline stays in sync
    private string _lastTextColor      = "000000";
    private string _lastHighlightColor = "FFFF00";

    // Full Word-style palette: 10 theme + 10 standard + 10 shades = shown as flat 40-color grid
    private static readonly (string Hex, string Name)[] ColorPalette =
    {
        // Row 1 — standard spectrum
        ("000000","Black"),      ("7F7F7F","Dark Gray"), ("BFBFBF","Light Gray"), ("FFFFFF","White"),
        ("FF0000","Red"),        ("FF4500","Orange Red"),("FFA500","Orange"),      ("FFD700","Gold"),
        ("FFFF00","Yellow"),     ("ADFF2F","Yellow Green"),
        // Row 2
        ("00FF00","Lime"),       ("008000","Green"),     ("006400","Dark Green"),  ("00FFFF","Cyan"),
        ("008B8B","Dark Cyan"),  ("0000FF","Blue"),      ("00008B","Dark Blue"),   ("8B008B","Dark Magenta"),
        ("FF00FF","Magenta"),    ("FF69B4","Hot Pink"),
        // Row 3 — muted/office tones
        ("C00000","Dark Red"),   ("E26B0A","Pumpkin"),   ("F4B942","Warm Yellow"),("70AD47","Soft Green"),
        ("4BACC6","Sky Blue"),   ("4472C4","Cornflower"),("7030A0","Purple"),      ("D9D9D9","Light Grey"),
        ("A9A9A9","Gray"),       ("595959","Charcoal"),
        // Row 4 — pastels
        ("FFE4E1","Misty Rose"), ("FFF0D1","Peach"),     ("FFFACD","Lemon"),      ("F0FFF0","Honeydew"),
        ("E0FFFF","Light Cyan"), ("E6E6FA","Lavender"),  ("FFE4FF","Thistle"),    ("FFF5EE","Seashell"),
        ("F5F5DC","Beige"),      ("FAEBD7","Antique"),
    };

    // Highlight colors match Word's exact 15 highlight options
    private static readonly (string Hex, string Name)[] HighlightPalette =
    {
        ("FFFF00","Yellow"),  ("00FF00","Bright Green"), ("00FFFF","Turquoise"),
        ("FF00FF","Pink"),    ("0000FF","Blue"),          ("FF0000","Red"),
        ("00008B","Dark Blue"),("008B8B","Teal"),         ("008000","Green"),
        ("800080","Violet"),  ("8B0000","Dark Red"),      ("808000","Dark Yellow"),
        ("808080","Gray 50%"),("C0C0C0","Gray 25%"),      ("FFFFFF","White"),
    };

    public DocxView()
    {
        InitializeComponent();
        PopulateColorGrid(TextColorGrid,      ColorPalette);
        PopulateColorGrid(HighlightColorGrid, HighlightPalette);
    }

    private static void PopulateColorGrid(GridView grid, (string Hex, string Name)[] palette)
    {
        foreach (var (hex, name) in palette)
        {
            var r = Convert.ToByte(hex[0..2], 16);
            var g = Convert.ToByte(hex[2..4], 16);
            var b = Convert.ToByte(hex[4..6], 16);
            var item = new WinBorder
            {
                Width = 20, Height = 20,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(WinColor.FromArgb(255, r, g, b)),
                Tag = hex
            };
            ToolTipService.SetToolTip(item, name);
            grid.Items.Add(item);
        }
    }

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
        SaveDocxBtn.IsEnabled = true;
        BtnSave.IsEnabled = true;
        BtnSaveAs.IsEnabled = true;
        BtnFindReplace.IsEnabled = true;
        BtnMergeDocx.IsEnabled = _vm.Files.Count(f => f.IsSelected &&
            ViewHelpers.IsDocx(f.FilePath)) >= 2;

        try
        {
            await DocWebView.EnsureCoreWebView2Async();

            // Subscribe once
            DocWebView.CoreWebView2.WebMessageReceived -= OnWebMessage;
            DocWebView.CoreWebView2.WebMessageReceived += OnWebMessage;
            DocWebView.NavigationCompleted -= OnNavigationCompleted;
            DocWebView.NavigationCompleted += OnNavigationCompleted;

            var html = ConvertDocxToHtml(path);
            DocWebView.NavigateToString(html);
            StatusText.Text = $"Loaded {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error loading: {ex.Message}";
        }
    }

    // Inject the selectionchange reporter once the page is ready
    private async void OnNavigationCompleted(WebView2 sender,
        Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
    {
        const string js = """
            document.addEventListener('selectionchange', function() {
                var s = {
                    bold:      document.queryCommandState('bold'),
                    italic:    document.queryCommandState('italic'),
                    underline: document.queryCommandState('underline'),
                    strike:    document.queryCommandState('strikeThrough'),
                    ul:        document.queryCommandState('insertUnorderedList'),
                    ol:        document.queryCommandState('insertOrderedList'),
                    justifyLeft:    document.queryCommandState('justifyLeft'),
                    justifyCenter:  document.queryCommandState('justifyCenter'),
                    justifyRight:   document.queryCommandState('justifyRight'),
                    justifyFull:    document.queryCommandState('justifyFull')
                };
                window.chrome.webview.postMessage(JSON.stringify(s));
            });
            """;
        await sender.ExecuteScriptAsync(js).AsTask();
    }

    // Receive the selection-state JSON and sync toggle buttons
    private void OnWebMessage(object? sender,
        Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                var json = e.TryGetWebMessageAsString();
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var r = doc.RootElement;
                bool Get(string k) => r.TryGetProperty(k, out var v) && v.GetBoolean();

                BoldBtn.IsChecked      = Get("bold");
                ItalicBtn.IsChecked    = Get("italic");
                UnderlineBtn.IsChecked = Get("underline");
                StrikeBtn.IsChecked    = Get("strike");
                BulletsBtn.IsChecked   = Get("ul");
                NumbersBtn.IsChecked   = Get("ol");
                AlignLeftBtn.IsChecked    = Get("justifyLeft");
                AlignCenterBtn.IsChecked  = Get("justifyCenter");
                AlignRightBtn.IsChecked   = Get("justifyRight");
                AlignJustifyBtn.IsChecked = Get("justifyFull");
            }
            catch { /* ignore malformed messages */ }
        });
    }

    public void Clear()
    {
        _currentFilePath = null;
        FileNameBlock.Text = "";
        SaveDocxBtn.IsEnabled = false;
        BtnSave.IsEnabled = false;
        BtnSaveAs.IsEnabled = false;
        BtnFindReplace.IsEnabled = false;
        BtnMergeDocx.IsEnabled = false;
        StatusText.Text = "";
        ResetToolbarState();
        try { DocWebView.NavigateToString("<html><body></body></html>"); } catch { }
    }

    public void RefreshButtons()
    {
        var selDocx = _vm.Files.Where(f => f.IsSelected && ViewHelpers.IsDocx(f.FilePath)).ToList();
        BtnMergeDocx.IsEnabled = selDocx.Count >= 2;
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    private async void SaveDocx_Click(object s, RoutedEventArgs e)
    {
        if (_currentFilePath is null) return;
        await SaveToPath(_currentFilePath);
    }

    private async void SaveDocxAs_Click(object s, RoutedEventArgs e)
    {
        if (_currentFilePath is null) return;
        var out_ = await ViewHelpers.SaveFile(_win,
            Path.GetFileName(_currentFilePath), ".docx");
        if (out_ is null) return;
        await SaveToPath(out_);
    }

    private async Task SaveToPath(string destPath)
    {
        try
        {
            var bodyHtml = await DocWebView.ExecuteScriptAsync(
                "document.body.innerHTML").AsTask();
            bodyHtml = System.Text.Json.JsonSerializer.Deserialize<string>(bodyHtml) ?? "";

            await Task.Run(() => SaveHtmlToDocx(bodyHtml, _currentFilePath!, destPath));
            StatusText.Text = $"Saved {Path.GetFileName(destPath)}";
            _vm.StatusText  = $"Saved {Path.GetFileName(destPath)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error saving: {ex.Message}";
        }
    }

    // ── Toolbar — execCommand wrappers ────────────────────────────────────────

    private async void BoldBtn_Click(object s, RoutedEventArgs e)
        => await ExecCmdAndSync("bold");

    private async void ItalicBtn_Click(object s, RoutedEventArgs e)
        => await ExecCmdAndSync("italic");

    private async void UnderlineBtn_Click(object s, RoutedEventArgs e)
        => await ExecCmdAndSync("underline");

    private async void StrikeBtn_Click(object s, RoutedEventArgs e)
        => await ExecCmdAndSync("strikeThrough");

    private async void AlignLeftBtn_Click(object s, RoutedEventArgs e)
        => await ExecCmdAndSync("justifyLeft");

    private async void AlignCenterBtn_Click(object s, RoutedEventArgs e)
        => await ExecCmdAndSync("justifyCenter");

    private async void AlignRightBtn_Click(object s, RoutedEventArgs e)
        => await ExecCmdAndSync("justifyRight");

    private async void AlignJustifyBtn_Click(object s, RoutedEventArgs e)
        => await ExecCmdAndSync("justifyFull");

    private async void BulletsBtn_Click(object s, RoutedEventArgs e)
        => await ExecCmdAndSync("insertUnorderedList");

    private async void NumbersBtn_Click(object s, RoutedEventArgs e)
        => await ExecCmdAndSync("insertOrderedList");

    private async void IndentBtn_Click(object s, RoutedEventArgs e)
        => await ExecCmd("indent");

    private async void OutdentBtn_Click(object s, RoutedEventArgs e)
        => await ExecCmd("outdent");

    private async void FontCombo_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (FontCombo.SelectedItem is not string font) return;
        await DocWebView.ExecuteScriptAsync(
            $"document.execCommand('fontName', false, '{font}')").AsTask();
    }

    private async void FontSizeCombo_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (FontSizeCombo.SelectedItem is not string sizeStr) return;
        var js = "document.execCommand('styleWithCSS', false, true);" +
                 "document.execCommand('fontSize', false, '7');" +
                 "var spans = document.querySelectorAll('font[size=\"7\"]');" +
                 "spans.forEach(function(el){" +
                 "  el.removeAttribute('size');" +
                 "  el.style.fontSize = '" + sizeStr + "pt';" +
                 "});";
        await DocWebView.ExecuteScriptAsync(js).AsTask();
    }

    // Runs a command then immediately re-queries the document state so toggles reflect reality
    private async Task ExecCmdAndSync(string cmd)
    {
        await ExecCmd(cmd);
        await SyncToolbarAsync();
    }

    private Task ExecCmd(string cmd)
        => DocWebView.ExecuteScriptAsync(
            $"document.execCommand('{cmd}', false, null)").AsTask();

    private async Task SyncToolbarAsync()
    {
        const string js = """
            JSON.stringify({
                bold:          document.queryCommandState('bold'),
                italic:        document.queryCommandState('italic'),
                underline:     document.queryCommandState('underline'),
                strike:        document.queryCommandState('strikeThrough'),
                ul:            document.queryCommandState('insertUnorderedList'),
                ol:            document.queryCommandState('insertOrderedList'),
                justifyLeft:   document.queryCommandState('justifyLeft'),
                justifyCenter: document.queryCommandState('justifyCenter'),
                justifyRight:  document.queryCommandState('justifyRight'),
                justifyFull:   document.queryCommandState('justifyFull')
            })
            """;
        var raw = await DocWebView.ExecuteScriptAsync(js).AsTask();
        var json = System.Text.Json.JsonSerializer.Deserialize<string>(raw) ?? "{}";
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var r = doc.RootElement;
        bool Get(string k) => r.TryGetProperty(k, out var v) && v.GetBoolean();

        BoldBtn.IsChecked         = Get("bold");
        ItalicBtn.IsChecked       = Get("italic");
        UnderlineBtn.IsChecked    = Get("underline");
        StrikeBtn.IsChecked       = Get("strike");
        BulletsBtn.IsChecked      = Get("ul");
        NumbersBtn.IsChecked      = Get("ol");
        AlignLeftBtn.IsChecked    = Get("justifyLeft");
        AlignCenterBtn.IsChecked  = Get("justifyCenter");
        AlignRightBtn.IsChecked   = Get("justifyRight");
        AlignJustifyBtn.IsChecked = Get("justifyFull");
    }

    private void ResetToolbarState()
    {
        BoldBtn.IsChecked = ItalicBtn.IsChecked = UnderlineBtn.IsChecked =
        StrikeBtn.IsChecked = BulletsBtn.IsChecked = NumbersBtn.IsChecked =
        AlignLeftBtn.IsChecked = AlignCenterBtn.IsChecked =
        AlignRightBtn.IsChecked = AlignJustifyBtn.IsChecked = false;
    }

    // ── Color & Highlight handlers ────────────────────────────────────────────

    private async void TextColorGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not WinBorder b || b.Tag is not string hex) return;
        TextColorFlyout.Hide();
        _lastTextColor = hex;
        var r = Convert.ToByte(hex[0..2], 16);
        var g = Convert.ToByte(hex[2..4], 16);
        var bv = Convert.ToByte(hex[4..6], 16);
        TextColorSwatch.Fill = new SolidColorBrush(WinColor.FromArgb(255, r, g, bv));
        await DocWebView.ExecuteScriptAsync(
            $"document.execCommand('styleWithCSS', false, true);" +
            $"document.execCommand('foreColor', false, '#{hex}');").AsTask();
    }

    private async void HighlightColorGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not WinBorder b || b.Tag is not string hex) return;
        HighlightColorFlyout.Hide();
        _lastHighlightColor = hex;
        var r = Convert.ToByte(hex[0..2], 16);
        var g = Convert.ToByte(hex[2..4], 16);
        var bv = Convert.ToByte(hex[4..6], 16);
        HighlightColorSwatch.Fill = new SolidColorBrush(WinColor.FromArgb(255, r, g, bv));
        await DocWebView.ExecuteScriptAsync(
            $"document.execCommand('styleWithCSS', false, true);" +
            $"document.execCommand('backColor', false, '#{hex}');").AsTask();
    }

    private async void HighlightNone_Click(object sender, RoutedEventArgs e)
    {
        HighlightColorFlyout.Hide();
        HighlightColorSwatch.Fill = new SolidColorBrush(Colors.Transparent);
        await DocWebView.ExecuteScriptAsync(
            "document.execCommand('styleWithCSS', false, true);" +
            "document.execCommand('backColor', false, 'transparent');").AsTask();
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

    // ── HTML → DOCX save-back ─────────────────────────────────────────────────

    /// <summary>
    /// Opens a DOCX file and converts its body content to an editable HTML page string.
    /// </summary>
    private static string ConvertDocxToHtml(string path)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset='utf-8'>" +
                  "<style>body{font-family:Calibri,sans-serif;font-size:11pt;margin:20px;}" +
                  "h1{font-size:2em}h2{font-size:1.5em}h3{font-size:1.17em}" +
                  "h4{font-size:1em}h5{font-size:.83em}h6{font-size:.67em}</style></head>" +
                  "<body contenteditable='true'>");

        using var wdoc = WordprocessingDocument.Open(path, false);
        var body = wdoc.MainDocumentPart?.Document?.Body;
        if (body is not null)
        {
            foreach (var elem in body.Elements())
            {
                if (elem is Paragraph para)
                {
                    sb.Append(ParagraphToHtml(para));
                }
                else if (elem is DocumentFormat.OpenXml.Wordprocessing.Table table)
                {
                    sb.Append("<table border='1' style='border-collapse:collapse;width:100%'>");
                    foreach (var row in table.Elements<TableRow>())
                    {
                        sb.Append("<tr>");
                        foreach (var cell in row.Elements<TableCell>())
                        {
                            sb.Append("<td style='padding:4px'>");
                            foreach (var cp in cell.Elements<Paragraph>())
                                sb.Append(ParagraphToHtml(cp));
                            sb.Append("</td>");
                        }
                        sb.Append("</tr>");
                    }
                    sb.Append("</table>");
                }
            }
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string ParagraphToHtml(Paragraph para)
    {
        var pPr = para.ParagraphProperties;
        var styleId = pPr?.ParagraphStyleId?.Val?.Value?.ToLowerInvariant() ?? "";
        string tag = styleId switch
        {
            "heading1" => "h1", "heading2" => "h2", "heading3" => "h3",
            "heading4"  => "h4", "heading5"  => "h5", "heading6"  => "h6",
            _ => "p"
        };

        var alignAttr = "";
        var jc = pPr?.Justification?.Val;
        if (jc is not null)
        {
            var jcStr = jc.Value.ToString();
            if (jcStr == JustificationValues.Center.ToString())
                alignAttr = " style='text-align:center'";
            else if (jcStr == JustificationValues.Right.ToString())
                alignAttr = " style='text-align:right'";
            else if (jcStr == JustificationValues.Both.ToString())
                alignAttr = " style='text-align:justify'";
        }

        var inner = new StringBuilder();
        foreach (var run in para.Elements<Run>())
        {
            var rPr = run.RunProperties;
            bool bold      = rPr?.Bold != null;
            bool italic    = rPr?.Italic != null;
            bool underline = rPr?.Underline != null;
            bool strike    = rPr?.Strike != null;
            var fontName   = rPr?.RunFonts?.Ascii?.Value;
            var fontSizeHH = rPr?.FontSize?.Val?.Value; // half-points
            var color      = rPr?.Color?.Val?.Value;

            var style = new StringBuilder();
            if (!string.IsNullOrEmpty(fontName))  style.Append($"font-family:'{fontName}';");
            if (!string.IsNullOrEmpty(fontSizeHH) && int.TryParse(fontSizeHH, out int hh))
                style.Append($"font-size:{hh / 2}pt;");
            if (!string.IsNullOrEmpty(color) && color != "auto")
                style.Append($"color:#{color};");
            if (bold)      style.Append("font-weight:bold;");
            if (italic)    style.Append("font-style:italic;");
            if (underline) style.Append("text-decoration:underline;");
            if (strike)    style.Append("text-decoration:line-through;");

            var text = string.Concat(run.Elements<DocumentFormat.OpenXml.Wordprocessing.Text>()
                .Select(t => t.Text))
                .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

            // Handle line breaks
            var brCount = run.Elements<Break>().Count();
            for (int b = 0; b < brCount; b++) inner.Append("<br>");

            if (string.IsNullOrEmpty(text) && brCount == 0) continue;

            if (style.Length > 0)
                inner.Append($"<span style='{style}'>{text}</span>");
            else
                inner.Append(text);
        }

        return $"<{tag}{alignAttr}>{inner}</{tag}>";
    }

    /// <summary>
    /// Opens the original DOCX, clears the body, and writes back paragraphs/tables
    /// that were edited in the WebView2 contenteditable HTML.
    /// The strategy: preserve all non-body parts (styles, numbering, images, etc.)
    /// from the original file, and rebuild the body from the edited HTML.
    /// </summary>
    private static void SaveHtmlToDocx(string bodyHtml, string sourcePath, string destPath)
    {
        // Copy original so we keep all embedded parts (images, styles, numbering…)
        if (!string.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase))
            File.Copy(sourcePath, destPath, true);

        using var wdoc = WordprocessingDocument.Open(destPath, true);
        var mainPart = wdoc.MainDocumentPart!;
        var body = mainPart.Document!.Body!;

        // Remove all existing body content except the final SectionProperties
        var sectPr = body.Elements<SectionProperties>().LastOrDefault()?.CloneNode(true);
        body.RemoveAllChildren();

        // Parse the HTML and rebuild paragraphs
        ParseHtmlToBody(bodyHtml, body);

        // Re-append section properties if present
        if (sectPr is not null)
            body.AppendChild(sectPr);

        mainPart.Document.Save();
    }

    private static void ParseHtmlToBody(string html, Body body)
    {
        // Very lightweight HTML parser: split on block-level tags
        // and convert inline formatting to RunProperties.
        // Handles <p>, <h1>-<h6>, <ul>/<ol>/<li>, <br>, and inline
        // <b>/<strong>, <i>/<em>, <u>, <s>/<strike>, <span style="…">.

        var tokens = TokenizeHtml(html);
        var stack = new Stack<string>();
        var paraRuns = new List<(string text, InlineStyle style)>();
        var currentStyle = new InlineStyle();
        bool inListItem = false;
        bool isOrdered = false;
        string blockTag = "p";

        void FlushParagraph()
        {
            if (!inListItem && paraRuns.Count == 0 && blockTag == "p") return;
            var para = new Paragraph();

            // Paragraph properties
            var pPr = BuildParagraphProperties(blockTag, currentStyle);
            if (pPr is not null) para.AppendChild(pPr);

            foreach (var (text, st) in paraRuns)
            {
                var run = new Run();
                var rPr = st.ToRunProperties();
                if (rPr is not null) run.AppendChild(rPr);
                run.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Text(text)
                    { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve });
                para.AppendChild(run);
            }
            body.AppendChild(para);
            paraRuns.Clear();
        }

        foreach (var token in tokens)
        {
            if (token.IsTag)
            {
                var tag = token.Tag.ToLowerInvariant();
                var isClose = tag.StartsWith('/');
                var bare = isClose ? tag[1..] : tag.Split(' ')[0];

                if (!isClose)
                {
                    switch (bare)
                    {
                        case "p": case "h1": case "h2": case "h3":
                        case "h4": case "h5": case "h6":
                            FlushParagraph();
                            blockTag = bare;
                            currentStyle = InlineStyle.FromTagAttributes(token.Tag);
                            break;
                        case "li":
                            FlushParagraph();
                            blockTag = "li";
                            inListItem = true;
                            break;
                        case "ul": isOrdered = false; break;
                        case "ol": isOrdered = true;  break;
                        case "br":
                            paraRuns.Add(("\n", currentStyle with { })); break;
                        case "b": case "strong":
                            currentStyle = currentStyle with { Bold = true }; break;
                        case "i": case "em":
                            currentStyle = currentStyle with { Italic = true }; break;
                        case "u":
                            currentStyle = currentStyle with { Underline = true }; break;
                        case "s": case "strike":
                            currentStyle = currentStyle with { Strike = true }; break;
                        case "span":
                            var spanStyle = InlineStyle.FromTagAttributes(token.Tag);
                            currentStyle = currentStyle.Merge(spanStyle);
                            stack.Push("span");
                            break;
                        case "font":
                            var fontStyle = InlineStyle.FromFontTag(token.Tag);
                            currentStyle = currentStyle.Merge(fontStyle);
                            stack.Push("font");
                            break;
                    }
                }
                else // close tag
                {
                    switch (bare)
                    {
                        case "p": case "h1": case "h2": case "h3":
                        case "h4": case "h5": case "h6":
                            FlushParagraph();
                            blockTag = "p";
                            currentStyle = new InlineStyle();
                            break;
                        case "li":
                            FlushParagraph();
                            inListItem = false;
                            blockTag = "p";
                            break;
                        case "b": case "strong":
                            currentStyle = currentStyle with { Bold = false }; break;
                        case "i": case "em":
                            currentStyle = currentStyle with { Italic = false }; break;
                        case "u":
                            currentStyle = currentStyle with { Underline = false }; break;
                        case "s": case "strike":
                            currentStyle = currentStyle with { Strike = false }; break;
                        case "span": case "font":
                            // pop style — simplified: reset only the attributes the span set
                            if (stack.TryPop(out _))
                                currentStyle = new InlineStyle();
                            break;
                    }
                }
            }
            else if (!string.IsNullOrEmpty(token.Text))
            {
                var decoded = HtmlDecode(token.Text);
                if (!string.IsNullOrEmpty(decoded))
                    paraRuns.Add((decoded, currentStyle with { }));
            }
        }
        FlushParagraph();
    }

    private static ParagraphProperties? BuildParagraphProperties(string blockTag, InlineStyle st)
    {
        var pPr = new ParagraphProperties();
        bool any = false;

        // Heading style
        if (blockTag is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
        {
            pPr.AppendChild(new ParagraphStyleId
                { Val = $"Heading{blockTag[1]}" });
            any = true;
        }

        // Alignment
        if (!string.IsNullOrEmpty(st.Align))
        {
            var jc = st.Align switch
            {
                "center"  => JustificationValues.Center,
                "right"   => JustificationValues.Right,
                "justify" => JustificationValues.Both,
                _         => JustificationValues.Left
            };
            pPr.AppendChild(new Justification { Val = jc });
            any = true;
        }

        return any ? pPr : null;
    }

    private static int CountOccurrences(string text, string find)
    {
        int count = 0, idx = 0;
        while ((idx = text.IndexOf(find, idx, StringComparison.Ordinal)) >= 0)
        { count++; idx += find.Length; }
        return count;
    }

    // ── Tokenizer (used by ParseHtmlToBody for save-back) ────────────────────

    private record HtmlToken(bool IsTag, string Tag, string Text);

    private static List<HtmlToken> TokenizeHtml(string html)
    {
        var tokens = new List<HtmlToken>();
        int i = 0;
        var sb = new StringBuilder();
        while (i < html.Length)
        {
            if (html[i] == '<')
            {
                if (sb.Length > 0) { tokens.Add(new(false, "", sb.ToString())); sb.Clear(); }
                int end = html.IndexOf('>', i);
                if (end < 0) break;
                var tag = html[(i + 1)..end];
                tokens.Add(new(true, tag, ""));
                i = end + 1;
            }
            else { sb.Append(html[i++]); }
        }
        if (sb.Length > 0) tokens.Add(new(false, "", sb.ToString()));
        return tokens;
    }

    private static string HtmlDecode(string text) =>
        text.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&quot;", "\"").Replace("&nbsp;", " ").Replace("\u00a0", " ");

    // ── Inline style record (used by ParseHtmlToBody for save-back) ──────────

    private record InlineStyle
    {
        public bool Bold { get; init; }
        public bool Italic { get; init; }
        public bool Underline { get; init; }
        public bool Strike { get; init; }
        public string? FontName { get; init; }
        public int FontSizePt { get; init; }
        public string? Color { get; init; }
        public string? Align { get; init; }

        public InlineStyle Merge(InlineStyle other) => new()
        {
            Bold       = Bold      || other.Bold,
            Italic     = Italic    || other.Italic,
            Underline  = Underline || other.Underline,
            Strike     = Strike    || other.Strike,
            FontName   = other.FontName   ?? FontName,
            FontSizePt = other.FontSizePt != 0 ? other.FontSizePt : FontSizePt,
            Color      = other.Color  ?? Color,
            Align      = other.Align  ?? Align
        };

        public static InlineStyle FromTagAttributes(string tag)
        {
            var st = new InlineStyle();
            var styleVal = ExtractAttr(tag, "style");
            if (string.IsNullOrEmpty(styleVal)) return st;
            foreach (var decl in styleVal.Split(';'))
            {
                var kv = decl.Split(':');
                if (kv.Length != 2) continue;
                var prop = kv[0].Trim().ToLowerInvariant();
                var val  = kv[1].Trim();
                st = prop switch
                {
                    "font-weight"     => val.Contains("bold")        ? st with { Bold = true }      : st,
                    "font-style"      => val.Contains("italic")      ? st with { Italic = true }    : st,
                    "text-decoration" => val.Contains("underline")   ? st with { Underline = true } :
                                        val.Contains("line-through") ? st with { Strike = true }    : st,
                    "font-family"     => st with { FontName  = val.Trim('\'', '"').Split(',')[0].Trim() },
                    "font-size"       => ParseFontSize(val) is int pt ? st with { FontSizePt = pt } : st,
                    "color"           => st with { Color = val.TrimStart('#') },
                    "text-align"      => st with { Align = val },
                    _                 => st
                };
            }
            return st;
        }

        public static InlineStyle FromFontTag(string tag)
        {
            var st = new InlineStyle();
            var face = ExtractAttr(tag, "face");
            if (!string.IsNullOrEmpty(face)) st = st with { FontName = face.Split(',')[0].Trim() };
            return st;
        }

        private static int? ParseFontSize(string val)
        {
            if (val.EndsWith("pt") && int.TryParse(val[..^2], out int pt)) return pt;
            if (val.EndsWith("px") && double.TryParse(val[..^2], out double px))
                return (int)Math.Round(px * 0.75);
            return null;
        }

        private static string ExtractAttr(string tag, string attr)
        {
            var key = $"{attr}=\"";
            var idx = tag.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "";
            idx += key.Length;
            var end = tag.IndexOf('"', idx);
            return end < 0 ? "" : tag[idx..end];
        }

        public RunProperties? ToRunProperties()
        {
            var rPr = new RunProperties();
            bool any = false;
            if (Bold)      { rPr.AppendChild(new Bold());    any = true; }
            if (Italic)    { rPr.AppendChild(new Italic());  any = true; }
            if (Underline) { rPr.AppendChild(new Underline { Val = UnderlineValues.Single }); any = true; }
            if (Strike)    { rPr.AppendChild(new Strike());  any = true; }
            if (!string.IsNullOrEmpty(FontName))
            {
                rPr.AppendChild(new RunFonts { Ascii = FontName, HighAnsi = FontName });
                any = true;
            }
            if (FontSizePt > 0)
            {
                rPr.AppendChild(new FontSize { Val = (FontSizePt * 2).ToString() });
                any = true;
            }
            if (!string.IsNullOrEmpty(Color))
            {
                rPr.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Color
                    { Val = Color.TrimStart('#') });
                any = true;
            }
            return any ? rPr : null;
        }
    }
}
