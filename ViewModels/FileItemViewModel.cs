namespace PaperlessDesktop.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

public partial class FileItemViewModel : ObservableObject
{
    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private string _fileType = "";
    [ObservableProperty] private string _pageCount = "";
    [ObservableProperty] private bool _isSelected = false;

    public FileItemViewModel(string path, string pageCount = "")
    {
        FilePath = path;
        FileName = Path.GetFileName(path);
        FileType = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        PageCount = pageCount;
    }

    public string DisplayName => string.IsNullOrEmpty(PageCount) ? FileName : $"{FileName}  [{PageCount}p]";

    // File type glyph (Segoe MDL2 Assets)
    public string FileTypeGlyph => FileType switch
    {
        "PDF"  => "\uE8A5",   // Document
        "DOCX" or "DOC" => "\uE8A5",
        "XLSX" or "XLS" or "CSV" => "\uE9F9",
        "PNG"  or "JPG" or "JPEG" or "BMP" or "GIF" or "WEBP" or "TIFF" => "\uEB9F",
        _ => "\uE8B7"         // Page
    };

    // Badge background color
    public SolidColorBrush FileTypeBadgeBrush => FileType switch
    {
        "PDF"  => new SolidColorBrush(Color.FromArgb(255, 255, 235, 235)),
        "DOCX" or "DOC" => new SolidColorBrush(Color.FromArgb(255, 220, 237, 255)),
        "XLSX" or "XLS" => new SolidColorBrush(Color.FromArgb(255, 220, 248, 225)),
        "CSV"  => new SolidColorBrush(Color.FromArgb(255, 230, 248, 230)),
        "PNG"  or "JPG" or "JPEG" or "BMP" or "GIF" or "WEBP" or "TIFF"
               => new SolidColorBrush(Color.FromArgb(255, 255, 246, 220)),
        _      => new SolidColorBrush(Color.FromArgb(255, 230, 230, 245))
    };

    // Badge text + icon foreground color
    public SolidColorBrush FileTypeIconBrush => FileType switch
    {
        "PDF"  => new SolidColorBrush(Color.FromArgb(255, 190, 50, 50)),
        "DOCX" or "DOC" => new SolidColorBrush(Color.FromArgb(255, 40, 100, 200)),
        "XLSX" or "XLS" => new SolidColorBrush(Color.FromArgb(255, 30, 140, 60)),
        "CSV"  => new SolidColorBrush(Color.FromArgb(255, 30, 120, 50)),
        "PNG"  or "JPG" or "JPEG" or "BMP" or "GIF" or "WEBP" or "TIFF"
               => new SolidColorBrush(Color.FromArgb(255, 180, 130, 30)),
        _      => new SolidColorBrush(Color.FromArgb(255, 90, 70, 180))
    };
}

