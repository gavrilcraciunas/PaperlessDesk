using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using PaperlessDesktop.Services;
using PaperlessDesktop.Shared;
using System.Collections.ObjectModel;

namespace PaperlessDesktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public  readonly LicenseService    License;
    private readonly PdfCoreService    _core;
    private readonly PdfConvertService _convert;
    private readonly BatchService      _batch;
    private readonly AppSettings       _settings;
    private readonly DispatcherQueue   _dispatcher;

    public ObservableCollection<FileItemViewModel> Files { get; } = new();

    [ObservableProperty] private string _statusText    = "Ready.";
    [ObservableProperty] private string _fileCountText = "0 files";
    [ObservableProperty] private bool   _isProcessing  = false;
    [ObservableProperty] private bool   _isDarkMode;

    public BatchResult? LastBatchResult { get; set; }

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    public MainViewModel(
        LicenseService    license,
        PdfCoreService    core,
        PdfConvertService convert,
        BatchService      batch,
        AppSettings       settings,
        DispatcherQueue   dispatcher)
    {
        License     = license;
        _core       = core;
        _convert    = convert;
        _batch      = batch;
        _settings   = settings;
        _dispatcher = dispatcher;
        _isDarkMode = settings.DarkMode;

        LoadRecentFiles();
    }

    // -------------------------------------------------------------------------
    // File list management
    // -------------------------------------------------------------------------

    public void AddFiles(IEnumerable<string> paths)
    {
        bool changed = false;

        foreach (var path in paths)
        {
            // Skip duplicates
            if (Files.Any(f => f.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                continue;

            var ext       = Path.GetExtension(path).ToLowerInvariant();
            var pageCount = "";

            if (ext == ".pdf")
            {
                try
                {
                    pageCount = PdfCoreService.GetPageCount(path).ToString();
                }
                catch
                {
                    pageCount = "?";
                }
            }

            Files.Add(new FileItemViewModel(path, pageCount));
            _settings.AddRecent(path);
            changed = true;
        }

        if (changed)
        {
            _settings.Save();
            UpdateFileCount();
        }
    }

    public void RemoveFile(FileItemViewModel item)
    {
        Files.Remove(item);
        UpdateFileCount();
    }

    public void ClearFiles()
    {
        Files.Clear();
        UpdateFileCount();
    }

    public void MoveUp(FileItemViewModel item)
    {
        var i = Files.IndexOf(item);
        if (i > 0) Files.Move(i, i - 1);
    }

    public void MoveDown(FileItemViewModel item)
    {
        var i = Files.IndexOf(item);
        if (i >= 0 && i < Files.Count - 1) Files.Move(i, i + 1);
    }

    // -------------------------------------------------------------------------
    // Selection helpers
    // -------------------------------------------------------------------------

    public IEnumerable<FileItemViewModel> SelectedPdfs =>
        Files.Where(f => f.IsSelected &&
                    f.FilePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));

    public IEnumerable<FileItemViewModel> SelectedImages =>
        Files.Where(f => f.IsSelected && IsImageExtension(f.FilePath));

    // AllOrSelected: if nothing is selected, fall back to all PDFs in the list
    public IEnumerable<FileItemViewModel> AllOrSelected(bool pdfOnly = true)
    {
        var sel = pdfOnly ? SelectedPdfs : Files.Where(f => f.IsSelected);
        if (sel.Any()) return sel;
        return pdfOnly
            ? Files.Where(f => f.FilePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            : Files;
    }

    private static bool IsImageExtension(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tiff" or ".webp";
    }

    // -------------------------------------------------------------------------
    // Status helpers
    // -------------------------------------------------------------------------

    public void SetStatus(string msg) =>
        _dispatcher.TryEnqueue(() => StatusText = msg);

    public void ClearProgress() =>
        _dispatcher.TryEnqueue(() => IsProcessing = false);

    partial void OnIsDarkModeChanged(bool value)
    {
        _settings.DarkMode = value;
        _settings.Save();
    }

    // -------------------------------------------------------------------------
    // Private
    // -------------------------------------------------------------------------

    private async void LoadRecentFiles()
    {
        var recent = _settings.RecentFiles.ToList();
        foreach (var path in recent)
        {
            if (!File.Exists(path)) continue;
            var ext       = Path.GetExtension(path).ToLowerInvariant();
            var pageCount = "";
            if (ext == ".pdf")
            {
                pageCount = await Task.Run(() =>
                {
                    try { return PdfCoreService.GetPageCount(path).ToString(); }
                    catch { return "?"; }
                });
            }
            Files.Add(new FileItemViewModel(path, pageCount));
        }
        UpdateFileCount();
    }

    private void UpdateFileCount() =>
        _dispatcher.TryEnqueue(() =>
            FileCountText = $"{Files.Count} file{(Files.Count == 1 ? "" : "s")}");
}
