namespace PaperlessDesktop.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

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
}

