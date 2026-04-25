using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using PaperlessDesktop.Dialogs;
using PaperlessDesktop.ViewModels;
using WinBorder = Microsoft.UI.Xaml.Controls.Border;
using WinColor  = Windows.UI.Color;
using WinColors = Microsoft.UI.Colors;
using WinFontFamily = Microsoft.UI.Xaml.Media.FontFamily;

namespace PaperlessDesktop.Controls;

// ─────────────────────────────────────────────────────────────────────────────
//  CellFormat – per-cell formatting applied during canvas rendering.
// ─────────────────────────────────────────────────────────────────────────────
internal record CellFormat
{
    public bool   Bold      { get; init; }
    public bool   Italic    { get; init; }
    public bool   Underline { get; init; }
    public string Align     { get; init; } = "left";   // left | center | right
    public string FontName  { get; init; } = "";
    public int    FontSize  { get; init; }              // 0 = default
    public string TextColor { get; init; } = "";        // hex RRGGBB or ""
    public string FillColor { get; init; } = "";        // hex RRGGBB or "" = no fill
}

// ─────────────────────────────────────────────────────────────────────────────
//  XlsxView  –  Excel-like spreadsheet viewer/editor for CSV and XLSX files.
// ─────────────────────────────────────────────────────────────────────────────
public sealed partial class XlsxView : UserControl
{
    // ── State ─────────────────────────────────────────────────────────────────

    private MainViewModel _vm  = null!;
    private Window        _win = null!;

    private string?              _currentFilePath;
    private string               _currentExt  = "";
    private List<List<string>>   _data        = new();
    private List<double>         _colWidths   = new();
    private List<string>         _sheets      = new();
    private int                  _activeSheet = 0;
    private bool                 _hasHeader   = true;

    // Per-cell formatting: key = (row, col)
    private Dictionary<(int, int), CellFormat> _formats = new();

    // Selection
    private int _selRow = -1;
    private int _selCol = -1;

    // Layout constants
    private const double ROW_H   = 26;
    private const double MIN_COL = 40;
    private const double DEF_COL = 110;

    // In-place editor
    private TextBox? _editor;
    private bool     _editing;

    // Column resize drag
    private bool   _resizing;
    private int    _resizeCol;
    private double _resizeStartX;
    private double _resizeStartW;

    // Freeze row
    private bool _freezeRow;

    // Multi-cell selection (anchor + active)
    private int _selRowAnchor = -1;
    private int _selColAnchor = -1;

    // Find & Replace state
    private List<(int r, int c)> _findMatches = new();
    private int _findIndex = -1;

    // Color palette (shared with DocxView style)
    private static readonly (string Hex, string Name)[] CellColorPalette =
    {
        ("000000","Black"),      ("7F7F7F","Dark Gray"), ("BFBFBF","Light Gray"), ("FFFFFF","White"),
        ("FF0000","Red"),        ("FF4500","Orange Red"),("FFA500","Orange"),      ("FFD700","Gold"),
        ("FFFF00","Yellow"),     ("ADFF2F","Yellow Green"),
        ("00FF00","Lime"),       ("008000","Green"),     ("006400","Dark Green"),  ("00FFFF","Cyan"),
        ("008B8B","Dark Cyan"),  ("0000FF","Blue"),      ("00008B","Dark Blue"),   ("8B008B","Dark Magenta"),
        ("FF00FF","Magenta"),    ("FF69B4","Hot Pink"),
        ("C00000","Dark Red"),   ("E26B0A","Pumpkin"),   ("F4B942","Warm Yellow"),("70AD47","Soft Green"),
        ("4BACC6","Sky Blue"),   ("4472C4","Cornflower"),("7030A0","Purple"),      ("D9D9D9","Light Grey"),
        ("A9A9A9","Gray"),       ("595959","Charcoal"),
        ("FFE4E1","Misty Rose"), ("FFF0D1","Peach"),     ("FFFACD","Lemon"),      ("F0FFF0","Honeydew"),
        ("E0FFFF","Light Cyan"), ("E6E6FA","Lavender"),  ("FFE4FF","Thistle"),    ("FFF5EE","Seashell"),
        ("F5F5DC","Beige"),      ("FAEBD7","Antique"),
    };

    // ── Init ──────────────────────────────────────────────────────────────────

    public XlsxView()
    {
        InitializeComponent();
        PopulateColorGrid(XlTextColorGrid, CellColorPalette);
        PopulateColorGrid(XlFillColorGrid, CellColorPalette);
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
        _vm  = vm;
        _win = win;
        CellCanvas.PointerPressed  += CellCanvas_PointerPressed;
        CellCanvas.PointerMoved    += CellCanvas_PointerMoved;
        CellCanvas.PointerReleased += CellCanvas_PointerReleased;
        ColHeaderCanvas.PointerPressed  += ColHeader_PointerPressed;
        ColHeaderCanvas.PointerMoved    += ColHeader_PointerMoved;
        ColHeaderCanvas.PointerReleased += ColHeader_PointerReleased;
        ColHeaderCanvas.IsHitTestVisible = true;
    }

    // ── Load / Unload ─────────────────────────────────────────────────────────

    public async void LoadFile(string path)
    {
        CommitEdit();
        _currentFilePath = path;
        _currentExt = Path.GetExtension(path).ToLowerInvariant();
        FileNameBlock.Text = Path.GetFileName(path);
        _selRow = -1; _selCol = -1;

        try
        {
            if (_currentExt == ".csv")
            {
                _sheets = new List<string> { "Sheet1" };
                var lines = await File.ReadAllLinesAsync(path);
                _data = lines.Select(l => ParseCsvLine(l).ToList()).ToList();
            }
            else
            {
                (_sheets, _data) = await Task.Run(() => ReadXlsxSheet(path, 0));
            }
        }
        catch (Exception ex)
        {
            _data = new List<List<string>> { new List<string> { $"Error: {ex.Message}" } };
            _sheets = new List<string> { "Error" };
        }

        _activeSheet = 0;
        PopulateSheetCombo();
        InitColWidths();
        Render();
        UpdateStats();
        SetButtonsEnabled(true);
    }

    public void Clear()
    {
        CommitEdit();
        _currentFilePath = null;
        _data = new(); _sheets = new(); _colWidths = new(); _formats = new();
        _selRow = -1; _selCol = -1;
        CellCanvas.Children.Clear();
        ColHeaderCanvas.Children.Clear();
        RowNumCanvas.Children.Clear();
        FileNameBlock.Text = ""; StatsBlock.Text = "";
        CellRefBox.Text = ""; FormulaBar.Text = "";
        SheetCombo.Items.Clear();
        SetButtonsEnabled(false);
    }

    // ── Sheet combo ───────────────────────────────────────────────────────────

    private void PopulateSheetCombo()
    {
        SheetCombo.SelectionChanged -= SheetCombo_SelectionChanged;
        SheetCombo.Items.Clear();
        foreach (var s in _sheets) SheetCombo.Items.Add(s);
        SheetCombo.SelectedIndex = _activeSheet;
        SheetCombo.Visibility = _sheets.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        SheetCombo.SelectionChanged += SheetCombo_SelectionChanged;
    }

    private async void SheetCombo_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (SheetCombo.SelectedIndex < 0 || _currentExt == ".csv") return;
        CommitEdit();
        _activeSheet = SheetCombo.SelectedIndex;
        try { (_, _data) = await Task.Run(() => ReadXlsxSheet(_currentFilePath!, _activeSheet)); }
        catch { return; }
        InitColWidths();
        _selRow = -1; _selCol = -1;
        CellRefBox.Text = ""; FormulaBar.Text = "";
        Render(); UpdateStats();
    }

    private void HeaderRowToggle_Click(object s, RoutedEventArgs e)
    {
        _hasHeader = HeaderRowToggle.IsChecked == true;
        Render(); UpdateStats();
    }

    // ── Column widths ─────────────────────────────────────────────────────────

    private void InitColWidths()
    {
        int cols = _data.Count > 0 ? _data.Max(r => r.Count) : 0;
        _colWidths = Enumerable.Repeat(DEF_COL, cols).ToList();
    }

    private double ColLeft(int col)
    {
        double x = 0;
        for (int i = 0; i < col; i++) x += _colWidths[i];
        return x;
    }

    private int ColAtX(double x)
    {
        double acc = 0;
        for (int i = 0; i < _colWidths.Count; i++)
        {
            acc += _colWidths[i];
            if (x < acc) return i;
        }
        return Math.Max(0, _colWidths.Count - 1);
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    private void Render()
    {
        CellCanvas.Children.Clear();
        ColHeaderCanvas.Children.Clear();
        RowNumCanvas.Children.Clear();

        if (_data.Count == 0) { CellCanvas.Width = 0; CellCanvas.Height = 0; return; }

        RenderFrozenRow();

        int rows = _data.Count;
        int cols = _data.Max(r => r.Count);
        while (_colWidths.Count < cols) _colWidths.Add(DEF_COL);

        double totalW = ColLeft(cols);
        double totalH = rows * ROW_H;

        CellCanvas.Width       = totalW;  CellCanvas.Height       = totalH;
        ColHeaderCanvas.Width  = totalW;  ColHeaderCanvas.Height  = 28;
        RowNumCanvas.Width     = 50;      RowNumCanvas.Height     = totalH;

        int dataStart = _hasHeader ? 1 : 0;

        // ── Column headers ─────────────────────────────────────────────────
        for (int c = 0; c < cols; c++)
        {
            double x = ColLeft(c);
            double w = _colWidths[c];
            bool sel = c == _selCol;

            string label = (_hasHeader && _data.Count > 0 && c < _data[0].Count)
                ? _data[0][c] : ColName(c);

            var border = new WinBorder
            {
                Width  = w - 1, Height = 27,
                Background = sel
                    ? new SolidColorBrush(WinColor.FromArgb(255, 184, 214, 243))
                    : new SolidColorBrush(WinColor.FromArgb(255, 239, 239, 239)),
                BorderBrush = new SolidColorBrush(WinColor.FromArgb(255, 180, 180, 180)),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(5, 0, 5, 0),
                Child = new TextBlock
                {
                    Text = label, FontSize = 12,
                    FontWeight = _hasHeader
                        ? Microsoft.UI.Text.FontWeights.SemiBold
                        : Microsoft.UI.Text.FontWeights.Normal,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
            Canvas.SetLeft(border, x); Canvas.SetTop(border, 0);
            ColHeaderCanvas.Children.Add(border);

            // Resize handle
            var handle = new WinBorder
            {
                Width = 8, Height = 28,
                Background = new SolidColorBrush(WinColors.Transparent),
            };
            Canvas.SetLeft(handle, x + w - 5); Canvas.SetTop(handle, 0);
            Canvas.SetZIndex(handle, 5);
            ColHeaderCanvas.Children.Add(handle);
        }

        // ── Row numbers ────────────────────────────────────────────────────
        for (int r = dataStart; r < rows; r++)
        {
            bool sel = r == _selRow;
            var numBorder = new WinBorder
            {
                Width = 49, Height = ROW_H - 1,
                Background = sel
                    ? new SolidColorBrush(WinColor.FromArgb(255, 184, 214, 243))
                    : new SolidColorBrush(WinColor.FromArgb(255, 239, 239, 239)),
                BorderBrush = new SolidColorBrush(WinColor.FromArgb(255, 180, 180, 180)),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(2, 0, 4, 0),
                Child = new TextBlock
                {
                    Text = (_hasHeader ? r : r + 1).ToString(),
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(WinColor.FromArgb(255, 100, 100, 100))
                }
            };
            Canvas.SetLeft(numBorder, 0);
            Canvas.SetTop(numBorder, r * ROW_H);
            RowNumCanvas.Children.Add(numBorder);
        }

        // ── Data cells ─────────────────────────────────────────────────────
        int selR1 = _selRowAnchor >= 0 ? Math.Min(_selRow, _selRowAnchor) : _selRow;
        int selR2 = _selRowAnchor >= 0 ? Math.Max(_selRow, _selRowAnchor) : _selRow;
        int selC1 = _selColAnchor >= 0 ? Math.Min(_selCol, _selColAnchor) : _selCol;
        int selC2 = _selColAnchor >= 0 ? Math.Max(_selCol, _selColAnchor) : _selCol;
        bool isRange = _selRowAnchor >= 0 && (_selRowAnchor != _selRow || _selColAnchor != _selCol);

        for (int r = dataStart; r < rows; r++)
        {
            double y = r * ROW_H;
            bool rowSel = r == _selRow;

            for (int c = 0; c < cols; c++)
            {
                double x = ColLeft(c);
                double w = _colWidths[c];
                bool isSel    = rowSel && c == _selCol;
                bool isSelRow = rowSel;
                bool isSelCol = c == _selCol;
                bool inRange  = isRange && r >= selR1 && r <= selR2 && c >= selC1 && c <= selC2;
                string raw    = c < _data[r].Count ? _data[r][c] : "";
                string val    = raw.StartsWith('=') ? EvalCell(r, c) : raw;

                _formats.TryGetValue((r, c), out var fmt);

                // Background
                SolidColorBrush cellBg;
                if (isSel)
                    cellBg = new SolidColorBrush(WinColor.FromArgb(255, 198, 224, 180));
                else if (inRange)
                    cellBg = new SolidColorBrush(WinColor.FromArgb(255, 219, 234, 254));
                else if (!string.IsNullOrEmpty(fmt?.FillColor))
                    cellBg = HexBrush(fmt.FillColor);
                else if (isSelRow || isSelCol)
                    cellBg = new SolidColorBrush(WinColor.FromArgb(20, 100, 160, 240));
                else
                    cellBg = new SolidColorBrush(WinColors.White);

                // Text alignment
                var halign = (fmt?.Align ?? "left") switch
                {
                    "center" => HorizontalAlignment.Center,
                    "right"  => HorizontalAlignment.Right,
                    _        => HorizontalAlignment.Left
                };

                // Text formatting
                var tb = new TextBlock
                {
                    Text = val, FontSize = fmt?.FontSize > 0 ? fmt.FontSize : 13,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = halign,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FontWeight = fmt?.Bold == true ? FontWeights.Bold : FontWeights.Normal,
                    FontStyle  = fmt?.Italic == true ? Windows.UI.Text.FontStyle.Italic
                                                     : Windows.UI.Text.FontStyle.Normal,
                    TextDecorations = fmt?.Underline == true
                        ? Windows.UI.Text.TextDecorations.Underline
                        : Windows.UI.Text.TextDecorations.None,
                    Foreground = !string.IsNullOrEmpty(fmt?.TextColor)
                        ? HexBrush(fmt.TextColor)
                        : new SolidColorBrush(WinColors.Black),
                };
                if (!string.IsNullOrEmpty(fmt?.FontName))
                    tb.FontFamily = new WinFontFamily(fmt.FontName);

                var cell = new WinBorder
                {
                    Width = w - 1, Height = ROW_H - 1,
                    Background = cellBg,
                    BorderBrush = isSel
                        ? new SolidColorBrush(WinColor.FromArgb(255, 33, 115, 70))
                        : new SolidColorBrush(WinColor.FromArgb(255, 212, 212, 212)),
                    BorderThickness = isSel ? new Thickness(2) : new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(5, 0, 5, 0),
                    Tag = (r, c),
                    Child = tb
                };
                Canvas.SetLeft(cell, x);
                Canvas.SetTop(cell, y);
                CellCanvas.Children.Add(cell);
            }
        }
    }

    // ── Pointer – cell canvas ─────────────────────────────────────────────────

    private void CellCanvas_PointerPressed(object s, PointerRoutedEventArgs e)
    {
        CommitEdit();
        var pt = e.GetCurrentPoint(CellCanvas).Position;
        int r = (int)(pt.Y / ROW_H);
        int c = ColAtX(pt.X);
        int dataStart = _hasHeader ? 1 : 0;
        if (r < dataStart || r >= _data.Count) return;

        var props = e.GetCurrentPoint(CellCanvas).Properties;
        bool shift = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (shift && _selRowAnchor >= 0)
        {
            _selRow = r; _selCol = c;
        }
        else
        {
            _selRow = r; _selCol = c;
            _selRowAnchor = r; _selColAnchor = c;
        }
        UpdateFormulaBar(); SyncFormattingToolbar(); Render();
        DataScroll.Focus(FocusState.Programmatic);
    }

    private void CellCanvas_DoubleTapped(object s, DoubleTappedRoutedEventArgs e)
    {
        if (_selRow >= 0 && _selCol >= 0) StartEdit();
    }

    private void CellCanvas_PointerMoved(object s, PointerRoutedEventArgs e) { }
    private void CellCanvas_PointerReleased(object s, PointerRoutedEventArgs e) { }

    // ── Pointer – column header (resize + select) ─────────────────────────────

    private void ColHeader_PointerPressed(object s, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(ColHeaderCanvas).Position;
        // Resize handle?
        for (int c = 0; c < _colWidths.Count; c++)
        {
            double right = ColLeft(c) + _colWidths[c];
            if (Math.Abs(pt.X - right) < 6)
            {
                _resizing = true; _resizeCol = c;
                _resizeStartX = pt.X; _resizeStartW = _colWidths[c];
                ColHeaderCanvas.CapturePointer(e.Pointer);
                return;
            }
        }
        CommitEdit();
        _selCol = ColAtX(pt.X); _selRow = -1;
        UpdateFormulaBar(); Render();
    }

    private void ColHeader_PointerMoved(object s, PointerRoutedEventArgs e)
    {
        if (!_resizing) return;
        var pt = e.GetCurrentPoint(ColHeaderCanvas).Position;
        _colWidths[_resizeCol] = Math.Max(MIN_COL, _resizeStartW + (pt.X - _resizeStartX));
        Render();
    }

    private void ColHeader_PointerReleased(object s, PointerRoutedEventArgs e)
    {
        if (_resizing) { _resizing = false; ColHeaderCanvas.ReleasePointerCapture(e.Pointer); }
    }

    // ── Keyboard navigation ───────────────────────────────────────────────────

    private void DataScroll_KeyDown(object s, KeyRoutedEventArgs e)
    {
        if (_editing) return;
        int dataStart = _hasHeader ? 1 : 0;
        int rows = _data.Count;
        int cols = _colWidths.Count;
        if (_selRow < dataStart) { _selRow = dataStart; _selCol = 0; }

        bool shift = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        bool ctrl = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        // Ctrl+H = Find & Replace
        if (ctrl && e.Key == Windows.System.VirtualKey.H)
        {
            FindReplace_Click(s, e); e.Handled = true; return;
        }

        bool moved = true;
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Up:
                if (!shift) { _selRowAnchor = -1; _selColAnchor = -1; }
                else if (_selRowAnchor < 0) { _selRowAnchor = _selRow; _selColAnchor = _selCol; }
                if (_selRow > dataStart) _selRow--; break;
            case Windows.System.VirtualKey.Down:
                if (!shift) { _selRowAnchor = -1; _selColAnchor = -1; }
                else if (_selRowAnchor < 0) { _selRowAnchor = _selRow; _selColAnchor = _selCol; }
                if (_selRow < rows - 1) _selRow++; break;
            case Windows.System.VirtualKey.Left:
                if (!shift) { _selRowAnchor = -1; _selColAnchor = -1; }
                else if (_selRowAnchor < 0) { _selRowAnchor = _selRow; _selColAnchor = _selCol; }
                if (_selCol > 0) _selCol--; break;
            case Windows.System.VirtualKey.Right:
                if (!shift) { _selRowAnchor = -1; _selColAnchor = -1; }
                else if (_selRowAnchor < 0) { _selRowAnchor = _selRow; _selColAnchor = _selCol; }
                if (_selCol < cols - 1) _selCol++; break;
            case Windows.System.VirtualKey.Enter:
                _selRowAnchor = -1; _selColAnchor = -1;
                if (_selRow < rows - 1) _selRow++; break;
            case Windows.System.VirtualKey.Tab:
                _selRowAnchor = -1; _selColAnchor = -1;
                if (_selCol < cols - 1) _selCol++;
                else { _selCol = 0; if (_selRow < rows - 1) _selRow++; }
                break;
            case Windows.System.VirtualKey.Delete:
            case Windows.System.VirtualKey.Back:
                DeleteSelectionContent(); UpdateFormulaBar(); Render(); e.Handled = true; return;
            case Windows.System.VirtualKey.F2:
                StartEdit(); e.Handled = true; return;
            default:
                moved = false;
                var ch = (char)e.Key;
                if (!e.KeyStatus.IsMenuKeyDown && (char.IsLetterOrDigit(ch) || char.IsPunctuation(ch)))
                    StartEdit();
                break;
        }
        if (moved) { e.Handled = true; UpdateFormulaBar(); Render(); ScrollToSelection(); }
    }

    private void DeleteSelectionContent()
    {
        if (_selRowAnchor >= 0 && (_selRowAnchor != _selRow || _selColAnchor != _selCol))
        {
            int r1 = Math.Min(_selRow, _selRowAnchor), r2 = Math.Max(_selRow, _selRowAnchor);
            int c1 = Math.Min(_selCol, _selColAnchor), c2 = Math.Max(_selCol, _selColAnchor);
            for (int r = r1; r <= r2; r++)
                for (int c = c1; c <= c2; c++) SetCell(r, c, "");
        }
        else SetCell(_selRow, _selCol, "");
    }

    private void FormulaBar_KeyDown(object s, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            if (_selRow >= 0 && _selCol >= 0) SetCell(_selRow, _selCol, FormulaBar.Text);
            CommitEdit(); Render(); DataScroll.Focus(FocusState.Programmatic); e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            UpdateFormulaBar(); DataScroll.Focus(FocusState.Programmatic); e.Handled = true;
        }
    }

    private void ScrollToSelection()
    {
        if (_selRow < 0 || _selCol < 0) return;
        double y  = _selRow * ROW_H;
        double x  = ColLeft(_selCol);
        double sv = DataScroll.VerticalOffset;
        double sh = DataScroll.HorizontalOffset;
        double vh = DataScroll.ViewportHeight;
        double vw = DataScroll.ViewportWidth;
        if (y < sv)                   DataScroll.ChangeView(null, y, null, true);
        else if (y + ROW_H > sv + vh) DataScroll.ChangeView(null, y + ROW_H - vh, null, true);
        if (x < sh)                                         DataScroll.ChangeView(x, null, null, true);
        else if (x + _colWidths[_selCol] > sh + vw)        DataScroll.ChangeView(x + _colWidths[_selCol] - vw, null, null, true);
    }

    // ── Scroll sync ───────────────────────────────────────────────────────────

    private void DataScroll_ViewChanged(object? s, ScrollViewerViewChangedEventArgs e)
    {
        ColHeaderScroll.ChangeView(DataScroll.HorizontalOffset, null, null, true);
        RowNumScroll.ChangeView(null, DataScroll.VerticalOffset, null, true);
        if (_freezeRow)
            FrozenHeaderScroll.ChangeView(DataScroll.HorizontalOffset, null, null, true);
    }

    // ── In-place editor ───────────────────────────────────────────────────────

    private void StartEdit()
    {
        if (_selRow < 0 || _selCol < 0 || _editing) return;
        _editing = true;
        string val = (_selCol < _data[_selRow].Count) ? _data[_selRow][_selCol] : "";

        _editor = new TextBox
        {
            Text = val,
            FontSize = 13,
            Padding = new Thickness(3),
            Width = Math.Max(_colWidths[_selCol], 60),
            Height = ROW_H,
            BorderBrush = new SolidColorBrush(WinColor.FromArgb(255, 33, 115, 70)),
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(WinColors.White)
        };
        _editor.KeyDown  += Editor_KeyDown;
        _editor.LostFocus += (_, _) => CommitEdit();
        Canvas.SetLeft(_editor, ColLeft(_selCol));
        Canvas.SetTop(_editor, _selRow * ROW_H);
        CellCanvas.Children.Add(_editor);
        _editor.Focus(FocusState.Programmatic);
        _editor.SelectAll();
    }

    private void CommitEdit()
    {
        if (!_editing || _editor is null) return;
        _editing = false;
        if (_selRow >= 0 && _selCol >= 0) SetCell(_selRow, _selCol, _editor.Text);
        _editor.KeyDown -= Editor_KeyDown;
        if (CellCanvas.Children.Contains(_editor)) CellCanvas.Children.Remove(_editor);
        _editor = null;
        UpdateFormulaBar();
    }

    private void Editor_KeyDown(object s, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Enter:
                CommitEdit(); Render();
                if (_selRow < _data.Count - 1) _selRow++;
                UpdateFormulaBar(); Render(); e.Handled = true; break;
            case Windows.System.VirtualKey.Escape:
                _editing = false;
                if (_editor is not null && CellCanvas.Children.Contains(_editor))
                    CellCanvas.Children.Remove(_editor);
                _editor = null; Render(); e.Handled = true; break;
            case Windows.System.VirtualKey.Tab:
                CommitEdit(); Render();
                if (_selCol < _colWidths.Count - 1) _selCol++;
                UpdateFormulaBar(); Render(); e.Handled = true; break;
        }
    }

    // ── Data helpers ──────────────────────────────────────────────────────────

    private void SetCell(int row, int col, string value)
    {
        while (_data.Count <= row) _data.Add(new List<string>());
        while (_data[row].Count <= col) _data[row].Add("");
        _data[row][col] = value;
    }

    private void UpdateFormulaBar()
    {
        if (_selRow < 0 || _selCol < 0) { CellRefBox.Text = ""; FormulaBar.Text = ""; return; }
        CellRefBox.Text = $"{ColName(_selCol)}{(_hasHeader ? _selRow : _selRow + 1)}";
        FormulaBar.Text = (_selRow < _data.Count && _selCol < _data[_selRow].Count)
            ? _data[_selRow][_selCol] : "";
    }

    private static string ColName(int c)
    {
        string name = "";
        c++;
        while (c > 0) { c--; name = (char)('A' + c % 26) + name; c /= 26; }
        return name;
    }

    private void UpdateStats()
    {
        int rows = _data.Count;
        int cols = rows > 0 ? _data.Max(r => r.Count) : 0;
        int dataRows = _hasHeader ? Math.Max(0, rows - 1) : rows;
        StatsBlock.Text = $"{dataRows} rows × {cols} cols";
    }

    private void SetButtonsEnabled(bool on)
    {
        BtnAddRow.IsEnabled    = on;
        BtnDeleteRow.IsEnabled = on;
        BtnInsertRow.IsEnabled = on;
        BtnInsertCol.IsEnabled = on;
        BtnDeleteCol.IsEnabled = on;
        BtnFindReplace.IsEnabled = on;
        BtnSortAsc.IsEnabled   = on;
        BtnSortDesc.IsEnabled  = on;
        BtnSave.IsEnabled      = on;
        BtnExportCsv.IsEnabled  = on;
        BtnExportXlsx.IsEnabled = on;
    }

    // ── Action handlers ───────────────────────────────────────────────────────

    private void AddRow_Click(object s, RoutedEventArgs e)
    {
        int cols = _data.Count > 0 ? _data.Max(r => r.Count) : 1;
        _data.Add(Enumerable.Repeat("", cols).ToList());
        while (_colWidths.Count < cols) _colWidths.Add(DEF_COL);
        _selRow = _data.Count - 1; _selCol = 0;
        Render(); UpdateStats(); UpdateFormulaBar(); ScrollToSelection();
    }

    private void DeleteRow_Click(object s, RoutedEventArgs e)
    {
        int dataStart = _hasHeader ? 1 : 0;
        if (_selRow < dataStart || _selRow >= _data.Count) return;
        _data.RemoveAt(_selRow);
        if (_selRow >= _data.Count) _selRow = _data.Count - 1;
        Render(); UpdateStats(); UpdateFormulaBar();
    }

    private void InsertRow_Click(object s, RoutedEventArgs e)
    {
        int dataStart = _hasHeader ? 1 : 0;
        int insertAt  = _selRow >= dataStart ? _selRow : _data.Count;
        int cols = _data.Count > 0 ? _data.Max(r => r.Count) : 1;
        _data.Insert(insertAt, Enumerable.Repeat("", cols).ToList());

        // Shift format keys down
        var newFormats = new Dictionary<(int, int), CellFormat>();
        foreach (var kv in _formats)
            newFormats[kv.Key.Item1 >= insertAt ? (kv.Key.Item1 + 1, kv.Key.Item2) : kv.Key] = kv.Value;
        _formats = newFormats;

        _selRow = insertAt; _selCol = Math.Max(0, _selCol);
        Render(); UpdateStats(); UpdateFormulaBar(); ScrollToSelection();
    }

    private void InsertCol_Click(object s, RoutedEventArgs e)
    {
        int insertAt = _selCol >= 0 ? _selCol : 0;
        foreach (var row in _data)
            if (row.Count > insertAt) row.Insert(insertAt, "");
            else { while (row.Count < insertAt) row.Add(""); row.Add(""); }
        _colWidths.Insert(insertAt, DEF_COL);

        var newFormats = new Dictionary<(int, int), CellFormat>();
        foreach (var kv in _formats)
            newFormats[kv.Key.Item2 >= insertAt ? (kv.Key.Item1, kv.Key.Item2 + 1) : kv.Key] = kv.Value;
        _formats = newFormats;

        _selCol = insertAt;
        Render(); UpdateStats(); UpdateFormulaBar();
    }

    private void DeleteCol_Click(object s, RoutedEventArgs e)
    {
        if (_selCol < 0 || _colWidths.Count == 0) return;
        int col = _selCol;
        foreach (var row in _data)
            if (col < row.Count) row.RemoveAt(col);
        if (col < _colWidths.Count) _colWidths.RemoveAt(col);

        var newFormats = new Dictionary<(int, int), CellFormat>();
        foreach (var kv in _formats)
        {
            if (kv.Key.Item2 == col) continue;
            newFormats[kv.Key.Item2 > col ? (kv.Key.Item1, kv.Key.Item2 - 1) : kv.Key] = kv.Value;
        }
        _formats = newFormats;

        _selCol = Math.Min(col, _colWidths.Count - 1);
        Render(); UpdateStats(); UpdateFormulaBar();
    }

    private async void FindReplace_Click(object s, RoutedEventArgs e)
    {
        CommitEdit();
        var dialog = new FindReplaceDialog { XamlRoot = Content.XamlRoot };
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        string find = dialog.FindText;
        string replace = dialog.ReplaceText;
        bool matchCase = dialog.MatchCase;
        if (string.IsNullOrEmpty(find)) return;

        int count = 0;
        var comp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (int r = 0; r < _data.Count; r++)
            for (int c = 0; c < _data[r].Count; c++)
                if (_data[r][c].Contains(find, comp))
                {
                    _data[r][c] = matchCase
                        ? _data[r][c].Replace(find, replace)
                        : ReplaceCaseInsensitive(_data[r][c], find, replace);
                    count++;
                }

        Render();
        await ViewHelpers.Info(Content.XamlRoot, "Find & Replace",
            count == 0 ? "No matches found." : $"Replaced {count} occurrence(s).");
    }

    private static string ReplaceCaseInsensitive(string input, string find, string replace)
    {
        var sb = new StringBuilder();
        int start = 0;
        while (true)
        {
            int idx = input.IndexOf(find, start, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) { sb.Append(input, start, input.Length - start); break; }
            sb.Append(input, start, idx - start);
            sb.Append(replace);
            start = idx + find.Length;
        }
        return sb.ToString();
    }

    private void XlFreezeBtn_Click(object s, RoutedEventArgs e)
    {
        _freezeRow = XlFreezeBtn.IsChecked == true;
        RenderFrozenRow();
        FrozenHeaderScroll.Visibility = _freezeRow ? Visibility.Visible : Visibility.Collapsed;
        FrozenCorner.Visibility       = _freezeRow ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderFrozenRow()
    {
        FrozenHeaderCanvas.Children.Clear();
        if (!_freezeRow || _data.Count == 0) return;

        int cols = _data.Max(r => r.Count);
        while (_colWidths.Count < cols) _colWidths.Add(DEF_COL);
        double totalW = ColLeft(cols);
        FrozenHeaderCanvas.Width = totalW;

        for (int c = 0; c < cols; c++)
        {
            double x = ColLeft(c);
            double w = _colWidths[c];
            string val = c < _data[0].Count ? _data[0][c] : "";
            var border = new WinBorder
            {
                Width = w - 1, Height = 25,
                Background = new SolidColorBrush(WinColor.FromArgb(255, 210, 230, 255)),
                BorderBrush = new SolidColorBrush(WinColor.FromArgb(255, 155, 187, 224)),
                BorderThickness = new Thickness(0, 0, 1, 2),
                Padding = new Thickness(5, 0, 5, 0),
                Child = new TextBlock
                {
                    Text = val, FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
            Canvas.SetLeft(border, x); Canvas.SetTop(border, 0);
            FrozenHeaderCanvas.Children.Add(border);
        }
    }

    private void SortAsc_Click(object s, RoutedEventArgs e)  => SortByColumn(true);
    private void SortDesc_Click(object s, RoutedEventArgs e) => SortByColumn(false);

    private void SortByColumn(bool ascending)
    {
        if (_selCol < 0) { _ = ViewHelpers.Info(Content.XamlRoot, "Sort", "Click a column header first."); return; }
        int dataStart = _hasHeader ? 1 : 0;
        if (_data.Count <= dataStart) return;
        var dataRows = _data.Skip(dataStart).ToList();
        var sorted = (ascending
            ? dataRows.OrderBy(r => _selCol < r.Count ? r[_selCol] : "", StringComparer.OrdinalIgnoreCase)
            : dataRows.OrderByDescending(r => _selCol < r.Count ? r[_selCol] : "", StringComparer.OrdinalIgnoreCase))
            .ToList();
        for (int i = 0; i < sorted.Count; i++) _data[dataStart + i] = sorted[i];
        _selRow = -1; Render(); UpdateFormulaBar();
    }

    private async void Save_Click(object s, RoutedEventArgs e)
    {
        CommitEdit();
        if (_currentFilePath is null) return;
        try
        {
            if (_currentExt == ".csv") await SaveCsv(_currentFilePath);
            else SaveXlsx(_currentFilePath);
            await ViewHelpers.Info(Content.XamlRoot, "Saved", $"✓  File saved.\n{_currentFilePath}");
        }
        catch (Exception ex) { await ViewHelpers.Err(Content.XamlRoot, "Save Error", ex.Message); }
    }

    private async void ExportCsv_Click(object s, RoutedEventArgs e)
    {
        CommitEdit();
        if (_currentFilePath is null) return;
        var out_ = await ViewHelpers.SaveFile(_win,
            Path.GetFileNameWithoutExtension(_currentFilePath) + ".csv", ".csv");
        if (out_ is null) return;
        await SaveCsv(out_);
        await ViewHelpers.Info(Content.XamlRoot, "Export", $"✓  CSV saved.\n{out_}");
    }

    private async void ExportXlsx_Click(object s, RoutedEventArgs e)
    {
        CommitEdit();
        if (_currentFilePath is null) return;
        var out_ = await ViewHelpers.SaveFile(_win,
            Path.GetFileNameWithoutExtension(_currentFilePath) + ".xlsx", ".xlsx");
        if (out_ is null) return;
        SaveXlsx(out_);
        await ViewHelpers.Info(Content.XamlRoot, "Export", $"✓  XLSX saved.\n{out_}");
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private async Task SaveCsv(string path)
    {
        var lines = _data.Select(r =>
            string.Join(",", r.Select(c =>
                c.Contains(',') || c.Contains('"') || c.Contains('\n')
                    ? $"\"{c.Replace("\"", "\"\"")}\"" : c)));
        await File.WriteAllLinesAsync(path, lines);
    }

    private void SaveXlsx(string path)
    {
        using var doc = SpreadsheetDocument.Create(path,
            DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook);
        var wbPart = doc.AddWorkbookPart();
        wbPart.Workbook = new Workbook();
        var wsPart = wbPart.AddNewPart<WorksheetPart>();
        var sd = new SheetData();
        wsPart.Worksheet = new Worksheet(sd);

        uint rowIdx = 1;
        foreach (var rowData in _data)
        {
            var row = new Row { RowIndex = rowIdx };
            uint colIdx = 1;
            foreach (var cellText in rowData)
            {
                row.Append(new Cell
                {
                    CellReference = $"{ColName((int)(colIdx - 1))}{rowIdx}",
                    DataType = CellValues.String,
                    CellValue = new CellValue(cellText)
                });
                colIdx++;
            }
            sd.Append(row);
            rowIdx++;
        }

        var sheets = wbPart.Workbook.AppendChild(new Sheets());
        sheets.Append(new Sheet
        {
            Id      = wbPart.GetIdOfPart(wsPart),
            SheetId = 1,
            Name    = (_sheets.Count > _activeSheet ? _sheets[_activeSheet] : null) ?? "Sheet1"
        });
    }

    // ── XLSX reader ───────────────────────────────────────────────────────────

    private static (List<string> sheets, List<List<string>> data)
        ReadXlsxSheet(string path, int sheetIndex)
    {
        var sheets = new List<string>();
        var data   = new List<List<string>>();

        using var doc = SpreadsheetDocument.Open(path, false);
        var wbPart = doc.WorkbookPart;
        if (wbPart is null) return (sheets, data);

        var sheetElements = wbPart.Workbook.Sheets?.Elements<Sheet>().ToList() ?? new();
        foreach (var sh in sheetElements) sheets.Add(sh.Name?.Value ?? "Sheet");
        if (sheetElements.Count == 0) return (sheets, data);

        sheetIndex = Math.Min(sheetIndex, sheetElements.Count - 1);
        var sheetId = sheetElements[sheetIndex].Id?.Value;
        if (sheetId is null) return (sheets, data);

        var wsPart = (WorksheetPart)wbPart.GetPartById(sheetId);
        var ss = wbPart.SharedStringTablePart?.SharedStringTable
            ?.Elements<SharedStringItem>().Select(i => i.InnerText).ToArray()
            ?? Array.Empty<string>();

        foreach (var row in wsPart.Worksheet.Descendants<Row>())
        {
            var cells = row.Elements<Cell>().ToList();
            if (cells.Count == 0) { data.Add(new List<string>()); continue; }

            int maxCol = ColRefToIndex(cells[^1].CellReference?.Value ?? "") + 1;
            var rowData = Enumerable.Repeat("", maxCol).ToList();

            foreach (var cell in cells)
            {
                int col = ColRefToIndex(cell.CellReference?.Value ?? "");
                while (rowData.Count <= col) rowData.Add("");
                var val = cell.CellValue?.InnerText ?? "";
                if (cell.DataType?.Value == CellValues.SharedString
                    && int.TryParse(val, out int idx) && idx < ss.Length)
                    val = ss[idx];
                rowData[col] = val;
            }
            data.Add(rowData);
        }
        return (sheets, data);
    }

    private static int ColRefToIndex(string cellRef)
    {
        int col = 0;
        foreach (char ch in cellRef)
        {
            if (!char.IsLetter(ch)) break;
            col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return Math.Max(0, col - 1);
    }

    // ── Formatting toolbar ────────────────────────────────────────────────────

    private void XlBoldBtn_Click(object s, RoutedEventArgs e)
        => ApplyFormat(f => f with { Bold = !(f.Bold) });

    private void XlItalicBtn_Click(object s, RoutedEventArgs e)
        => ApplyFormat(f => f with { Italic = !(f.Italic) });

    private void XlUnderlineBtn_Click(object s, RoutedEventArgs e)
        => ApplyFormat(f => f with { Underline = !(f.Underline) });

    private void XlAlignLeftBtn_Click(object s, RoutedEventArgs e)
        => ApplyFormat(f => f with { Align = "left" });

    private void XlAlignCenterBtn_Click(object s, RoutedEventArgs e)
        => ApplyFormat(f => f with { Align = "center" });

    private void XlAlignRightBtn_Click(object s, RoutedEventArgs e)
        => ApplyFormat(f => f with { Align = "right" });

    private void XlFontCombo_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (XlFontCombo.SelectedItem is not string font) return;
        ApplyFormat(f => f with { FontName = font });
    }

    private void XlFontSizeCombo_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (XlFontSizeCombo.SelectedItem is not string sz) return;
        if (int.TryParse(sz, out int pt)) ApplyFormat(f => f with { FontSize = pt });
    }

    private void XlTextColorGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not WinBorder b || b.Tag is not string hex) return;
        XlTextColorFlyout.Hide();
        XlTextColorSwatch.Fill = HexBrush(hex);
        ApplyFormat(f => f with { TextColor = hex });
    }

    private void XlFillColorGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not WinBorder b || b.Tag is not string hex) return;
        XlFillColorFlyout.Hide();
        XlFillColorSwatch.Fill = HexBrush(hex);
        ApplyFormat(f => f with { FillColor = hex });
    }

    private void XlFillNone_Click(object sender, RoutedEventArgs e)
    {
        XlFillColorFlyout.Hide();
        XlFillColorSwatch.Fill = new SolidColorBrush(WinColors.Transparent);
        ApplyFormat(f => f with { FillColor = "" });
    }

    private void XlClearFormat_Click(object sender, RoutedEventArgs e)
    {
        if (_selRow < 0 || _selCol < 0) return;
        _formats.Remove((_selRow, _selCol));
        SyncFormattingToolbar(); Render();
    }

    // Applies a format mutation to the selected cell(s)
    private void ApplyFormat(Func<CellFormat, CellFormat> mutate)
    {
        if (_selRow < 0 || _selCol < 0) return;

        int r1 = _selRowAnchor >= 0 ? Math.Min(_selRow, _selRowAnchor) : _selRow;
        int r2 = _selRowAnchor >= 0 ? Math.Max(_selRow, _selRowAnchor) : _selRow;
        int c1 = _selColAnchor >= 0 ? Math.Min(_selCol, _selColAnchor) : _selCol;
        int c2 = _selColAnchor >= 0 ? Math.Max(_selCol, _selColAnchor) : _selCol;

        for (int r = r1; r <= r2; r++)
            for (int c = c1; c <= c2; c++)
            {
                var key = (r, c);
                var current = _formats.TryGetValue(key, out var f) ? f : new CellFormat();
                _formats[key] = mutate(current);
            }
        SyncFormattingToolbar(); Render();
    }

    // Syncs toggle button states from the selected cell's format
    private void SyncFormattingToolbar()
    {
        var fmt = (_selRow >= 0 && _selCol >= 0 && _formats.TryGetValue((_selRow, _selCol), out var f))
            ? f : new CellFormat();

        XlBoldBtn.IsChecked      = fmt.Bold;
        XlItalicBtn.IsChecked    = fmt.Italic;
        XlUnderlineBtn.IsChecked = fmt.Underline;
        XlAlignLeftBtn.IsChecked   = fmt.Align == "left"   || fmt.Align == "";
        XlAlignCenterBtn.IsChecked = fmt.Align == "center";
        XlAlignRightBtn.IsChecked  = fmt.Align == "right";

        if (!string.IsNullOrEmpty(fmt.FontName))
            XlFontCombo.SelectedItem = fmt.FontName;
        else
            XlFontCombo.SelectedItem = null;

        if (fmt.FontSize > 0)
            XlFontSizeCombo.SelectedItem = fmt.FontSize.ToString();
        else
            XlFontSizeCombo.SelectedItem = null;

        XlTextColorSwatch.Fill = !string.IsNullOrEmpty(fmt.TextColor)
            ? HexBrush(fmt.TextColor)
            : new SolidColorBrush(WinColors.Black);
        XlFillColorSwatch.Fill = !string.IsNullOrEmpty(fmt.FillColor)
            ? HexBrush(fmt.FillColor)
            : new SolidColorBrush(WinColors.Transparent);
    }

    // ── Formula evaluation ────────────────────────────────────────────────────

    private string EvalCell(int row, int col)
    {
        string raw = col < _data[row].Count ? _data[row][col] : "";
        if (!raw.StartsWith('=')) return raw;
        try { return EvalFormula(raw[1..]); }
        catch { return raw; }
    }

    private string EvalFormula(string expr)
    {
        expr = expr.Trim();
        // SUM(A1:B3) or SUM(A1,B2,...)
        var m = System.Text.RegularExpressions.Regex.Match(expr,
            @"^(SUM|AVERAGE|COUNT|MIN|MAX)\((.+)\)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success)
        {
            string fn = m.Groups[1].Value.ToUpperInvariant();
            var nums = ResolveArgs(m.Groups[2].Value);
            if (nums.Count == 0) return "0";
            return fn switch
            {
                "SUM"     => nums.Sum().ToString("G10"),
                "AVERAGE" => (nums.Sum() / nums.Count).ToString("G10"),
                "COUNT"   => nums.Count.ToString(),
                "MIN"     => nums.Min().ToString("G10"),
                "MAX"     => nums.Max().ToString("G10"),
                _         => "0"
            };
        }
        // IF(cond, true_val, false_val)
        var ifm = System.Text.RegularExpressions.Regex.Match(expr,
            @"^IF\((.+?),(.+?),(.+?)\)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (ifm.Success)
        {
            bool cond = EvalCondition(ifm.Groups[1].Value.Trim());
            return EvalFormula(cond ? ifm.Groups[2].Value.Trim() : ifm.Groups[3].Value.Trim());
        }
        // Direct cell ref e.g. A1
        if (System.Text.RegularExpressions.Regex.IsMatch(expr, @"^[A-Za-z]+\d+$"))
        {
            var (r, c) = CellRefToRowCol(expr);
            return r >= 0 ? EvalCell(r, c) : "";
        }
        // Arithmetic: simple number
        if (double.TryParse(expr, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double d))
            return d.ToString("G10");
        return expr;
    }

    private bool EvalCondition(string cond)
    {
        foreach (var op in new[] { ">=", "<=", "<>", ">", "<", "=" })
        {
            int i = cond.IndexOf(op, StringComparison.Ordinal);
            if (i < 0) continue;
            string lhs = EvalFormula(cond[..i].Trim());
            string rhs = EvalFormula(cond[(i + op.Length)..].Trim());
            if (double.TryParse(lhs, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double l) &&
                double.TryParse(rhs, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double r))
                return op switch { ">=" => l >= r, "<=" => l <= r, "<>" => l != r,
                    ">" => l > r, "<" => l < r, _ => l == r };
            return op switch { "<>" => lhs != rhs, "=" => lhs == rhs, _ => false };
        }
        return false;
    }

    private List<double> ResolveArgs(string args)
    {
        var nums = new List<double>();
        foreach (var token in args.Split(','))
        {
            var t = token.Trim();
            // Range A1:C3
            var rm = System.Text.RegularExpressions.Regex.Match(t, @"^([A-Za-z]+\d+):([A-Za-z]+\d+)$");
            if (rm.Success)
            {
                var (r1, c1) = CellRefToRowCol(rm.Groups[1].Value);
                var (r2, c2) = CellRefToRowCol(rm.Groups[2].Value);
                for (int r = Math.Min(r1,r2); r <= Math.Max(r1,r2); r++)
                    for (int c = Math.Min(c1,c2); c <= Math.Max(c1,c2); c++)
                    {
                        var v = EvalCell(r, c);
                        if (double.TryParse(v, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double d)) nums.Add(d);
                    }
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^[A-Za-z]+\d+$"))
            {
                var (r, c) = CellRefToRowCol(t);
                if (r >= 0) { var v = EvalCell(r, c);
                    if (double.TryParse(v, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double d)) nums.Add(d); }
            }
            else if (double.TryParse(t, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double d2)) nums.Add(d2);
        }
        return nums;
    }

    private (int row, int col) CellRefToRowCol(string cellRef)
    {
        var rm = System.Text.RegularExpressions.Regex.Match(cellRef, @"^([A-Za-z]+)(\d+)$");
        if (!rm.Success) return (-1, -1);
        int col = ColRefToIndex(rm.Groups[1].Value);
        int row = int.Parse(rm.Groups[2].Value) - 1;
        if (_hasHeader) row++;   // adjust: row 1 in formula = first data row (index 1 with header)
        if (row < 0 || row >= _data.Count || col < 0) return (-1, -1);
        return (row, col);
    }

    // ── Color helpers ─────────────────────────────────────────────────────────

    private static SolidColorBrush HexBrush(string hex)
    {
        var r = Convert.ToByte(hex[0..2], 16);
        var g = Convert.ToByte(hex[2..4], 16);
        var b = Convert.ToByte(hex[4..6], 16);
        return new SolidColorBrush(WinColor.FromArgb(255, r, g, b));
    }

    // ── CSV parser ────────────────────────────────────────────────────────────

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
}


