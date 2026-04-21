using PaperlessDesktop.Services;
using PaperlessDesktop.ViewModels;

namespace PaperlessDesktop.Controls;

public sealed partial class ImageView : UserControl
{
    private MainViewModel _vm = null!;
    private Window _win = null!;
    private string? _currentFilePath;

    public ImageView() => InitializeComponent();

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
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(stream);
            PreviewImage.Source = bmp;
            DimensionsBlock.Text = $"{bmp.PixelWidth} × {bmp.PixelHeight}";
        }
        catch { }
        RefreshButtons();
    }

    public void Clear()
    {
        _currentFilePath = null;
        PreviewImage.Source = null;
        FileNameBlock.Text = "";
        DimensionsBlock.Text = "";
        RefreshButtons();
    }

    public void RefreshButtons()
    {
        bool hasImage = _currentFilePath is not null;
        bool hasSelImages = _vm.SelectedImages.Any();
        BtnResize.IsEnabled = hasImage;
        BtnCrop.IsEnabled = hasImage;
        BtnRotateCW.IsEnabled = hasImage;
        BtnRotate180.IsEnabled = hasImage;
        BtnRotateCCW.IsEnabled = hasImage;
        BtnGrayscale.IsEnabled = hasImage;
        BtnImagesToPdf.IsEnabled = hasSelImages;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private LicenseService License => App.Services.GetRequiredService<LicenseService>();
    private PdfConvertService Convert => App.Services.GetRequiredService<PdfConvertService>();
    private XamlRoot Root => Content.XamlRoot;
    private bool Pro(string f) => ViewHelpers.RequirePro(Root, License, f);

    private async Task ApplyTransform(string op,
        uint? newW = null, uint? newH = null,
        uint cropX = 0, uint cropY = 0, uint cropW = 0, uint cropH = 0)
    {
        if (_currentFilePath is null) return;
        var ext = Path.GetExtension(_currentFilePath).ToLowerInvariant();
        var outExt = ext is ".jpg" or ".jpeg" ? ".jpg" : ".png";
        var out_ = await ViewHelpers.SaveFile(_win,
            $"{Path.GetFileNameWithoutExtension(_currentFilePath)}_edited{outExt}", outExt);
        if (out_ is null) return;

        await ViewHelpers.Run(Root, "Processing image…", async (_, ct) =>
        {
            var file = await StorageFile.GetFileFromPathAsync(_currentFilePath);
            using var inStream = await file.OpenReadAsync();
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(inStream);
            uint srcW = decoder.PixelWidth, srcH = decoder.PixelHeight;
            var transform = new Windows.Graphics.Imaging.BitmapTransform();

            switch (op)
            {
                case "Resize":
                    transform.ScaledWidth = newW!.Value;
                    transform.ScaledHeight = newH ?? (uint)(srcH * (double)newW!.Value / srcW);
                    transform.InterpolationMode = Windows.Graphics.Imaging.BitmapInterpolationMode.Fant;
                    break;
                case "Crop":
                    transform.Bounds = new Windows.Graphics.Imaging.BitmapBounds
                    { X = cropX, Y = cropY, Width = cropW, Height = cropH };
                    break;
                case "Rotate90CW":
                    transform.Rotation = Windows.Graphics.Imaging.BitmapRotation.Clockwise90Degrees;
                    break;
                case "Rotate180":
                    transform.Rotation = Windows.Graphics.Imaging.BitmapRotation.Clockwise180Degrees;
                    break;
                case "Rotate90CCW":
                    transform.Rotation = Windows.Graphics.Imaging.BitmapRotation.Clockwise270Degrees;
                    break;
            }

            var pixels = await decoder.GetPixelDataAsync(
                Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                transform,
                Windows.Graphics.Imaging.ExifOrientationMode.RespectExifOrientation,
                Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
            var pixelData = pixels.DetachPixelData();

            uint outW = transform.ScaledWidth > 0 ? transform.ScaledWidth : srcW;
            uint outH = transform.ScaledHeight > 0 ? transform.ScaledHeight : srcH;
            if (op == "Crop") { outW = cropW; outH = cropH; }
            else if (op is "Rotate90CW" or "Rotate90CCW") { outW = srcH; outH = srcW; }

            if (op == "Grayscale")
            {
                outW = srcW; outH = srcH;
                for (int i = 0; i < pixelData.Length; i += 4)
                {
                    byte gray = (byte)(0.299 * pixelData[i + 2] + 0.587 * pixelData[i + 1] + 0.114 * pixelData[i]);
                    pixelData[i] = pixelData[i + 1] = pixelData[i + 2] = gray;
                }
            }

            var outFolder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(out_)!);
            var outFile = await outFolder.CreateFileAsync(Path.GetFileName(out_),
                Windows.Storage.CreationCollisionOption.ReplaceExisting);
            using var outStream = await outFile.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
            var encoderId = outExt == ".jpg"
                ? Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId
                : Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId;
            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(encoderId, outStream);
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                outW, outH, decoder.DpiX, decoder.DpiY, pixelData);
            await encoder.FlushAsync();
        });

        await ViewHelpers.Info(Root, "Image Editor", $"✓  Image saved.\n{out_}");
    }

    // ── Action handlers ───────────────────────────────────────────────────────

    private async void Resize_Click(object s, RoutedEventArgs e)
    {
        var w = await ViewHelpers.AskInt(Root, "Resize", "New width (pixels):", 800); if (w is null) return;
        var h = await ViewHelpers.AskInt(Root, "Resize", "New height (pixels, 0 = auto aspect):", 0); if (h is null) return;
        await ApplyTransform("Resize", newW: (uint)w.Value, newH: h.Value == 0 ? null : (uint?)h.Value);
    }

    private async void Crop_Click(object s, RoutedEventArgs e)
    {
        var x = await ViewHelpers.AskInt(Root, "Crop", "Left offset (pixels):", 0); if (x is null) return;
        var y = await ViewHelpers.AskInt(Root, "Crop", "Top offset (pixels):", 0); if (y is null) return;
        var cw = await ViewHelpers.AskInt(Root, "Crop", "Crop width (pixels):", 400); if (cw is null) return;
        var ch = await ViewHelpers.AskInt(Root, "Crop", "Crop height (pixels):", 400); if (ch is null) return;
        await ApplyTransform("Crop", cropX: (uint)x.Value, cropY: (uint)y.Value,
            cropW: (uint)cw.Value, cropH: (uint)ch.Value);
    }

    private async void RotateCW_Click(object s, RoutedEventArgs e)
        => await ApplyTransform("Rotate90CW");

    private async void Rotate180_Click(object s, RoutedEventArgs e)
        => await ApplyTransform("Rotate180");

    private async void RotateCCW_Click(object s, RoutedEventArgs e)
        => await ApplyTransform("Rotate90CCW");

    private async void Grayscale_Click(object s, RoutedEventArgs e)
        => await ApplyTransform("Grayscale");

    private async void ImagesToPdf_Click(object s, RoutedEventArgs e)
    {
        if (!Pro("Images → PDF")) return;
        var images = _vm.SelectedImages.ToList();
        if (images.Count == 0) { await ViewHelpers.Info(Root, "Images → PDF", "Select at least one image."); return; }
        var out_ = await ViewHelpers.SaveFile(_win, "images.pdf", ".pdf"); if (out_ is null) return;
        ImagesToPdfResult? result = null;
        await ViewHelpers.Run(Root, $"Creating PDF from {images.Count} image(s)…", async (prog, ct) =>
        {
            result = await Convert.ImagesToPdfAsync(images.Select(f => f.FilePath), out_, progress: prog, ct: ct);
        });
        if (result is not null)
            await ViewHelpers.Info(Root, "Done", $"✓  PDF created from {result.ImageCount} image(s).\n{out_}");
    }
}
