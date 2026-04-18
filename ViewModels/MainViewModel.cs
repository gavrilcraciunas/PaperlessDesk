using PaperlessDesktop.Services;
using PaperlessDesktop.Shared;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PaperlessDesktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public readonly LicenseService License;
    private readonly PdfCoreService _core;
    private readonly PdfConvertService _convert;
    private readonly BatchService _batch;
    private readonly AppSettings _settings;
    private readonly DispatcherQueue _dispatcher;

    public ObservableCollection<FileItemViewModel> Files { get; } = new();

    [ObservableProperty] private string _statusText = "Ready.";
    [ObservableProperty] private string _fileCountText = "0 files";
    [ObservableProperty] private bool _isProcessing = false;
    [ObservableProperty] private int _progressValue = 0;
    [ObservableProperty] private int _progressMax = 100;
    [ObservableProperty] private string _progressLabel = "";
    [ObservableProperty] private bool _isDarkMode = false;
    public BatchResult? LastBatchResult { get; set; }

    public MainViewModel(LicenseService license, PdfCoreService core,
        PdfConvertService convert, BatchService batch,
        AppSettings settings, DispatcherQueue dispatcher)
    {
        License = license;
        _core = core;
        _convert = convert;
        _batch = batch;
        _settings = settings;
        _dispatcher = dispatcher;
        IsDarkMode = settings.DarkMode;
        LoadRecentFiles();
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Files.Any(f => f.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                continue;
            var pageCount = "";
            if (Path.GetExtension(path).ToLowerInvariant() == ".pdf")
            {
                try { pageCount = PdfCoreService.GetPageCount(path).ToString(); }
                catch { pageCount = "?"; }
            }
            Files.Add(new FileItemViewModel(path, pageCount));
            _settings.AddRecent(path);
        }
        _settings.Save();
        UpdateFileCount();
    }

    public void RemoveFile(FileItemViewModel item) { Files.Remove(item); UpdateFileCount(); }
    public void ClearFiles() { Files.Clear(); UpdateFileCount(); }

    public void MoveUp(FileItemViewModel item)
    { var i = Files.IndexOf(item); if (i > 0) Files.Move(i, i - 1); }

    public void MoveDown(FileItemViewModel item)
    { var i = Files.IndexOf(item); if (i >= 0 && i < Files.Count - 1) Files.Move(i, i + 1); }

    public IEnumerable<FileItemViewModel> SelectedPdfs =>
        Files.Where(f => f.IsSelected &&
            f.FilePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));

    public IEnumerable<FileItemViewModel> SelectedImages =>
        Files.Where(f => f.IsSelected &&
            new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".webp" }
                .Contains(Path.GetExtension(f.FilePath).ToLowerInvariant()));

    public IEnumerable<FileItemViewModel> AllOrSelected(bool pdfOnly = true)
    {
        var sel = pdfOnly ? SelectedPdfs : Files.Where(f => f.IsSelected);
        return sel.Any() ? sel
            : pdfOnly ? Files.Where(f => f.FilePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            : Files;
    }

    public void SetStatus(string msg) =>
        _dispatcher.TryEnqueue(() => StatusText = msg);

    public void ClearProgress() =>
        _dispatcher.TryEnqueue(() => { IsProcessing = false; ProgressValue = 0; ProgressLabel = ""; });

    partial void OnIsDarkModeChanged(bool value) { _settings.DarkMode = value; _settings.Save(); }

    private void LoadRecentFiles()
    {
        foreach (var path in _settings.RecentFiles)
            if (File.Exists(path)) Files.Add(new FileItemViewModel(path));
        UpdateFileCount();
    }

    private void UpdateFileCount() =>
        _dispatcher.TryEnqueue(() =>
            FileCountText = $"{Files.Count} file{(Files.Count == 1 ? "" : "s")}");
}

