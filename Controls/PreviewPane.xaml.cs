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
            PrevPageBtn.IsEnabled = false;
            NextPageBtn.IsEnabled = _totalPages > 1;
            await RenderPageAsync(_currentPage);
        }
        catch { }
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