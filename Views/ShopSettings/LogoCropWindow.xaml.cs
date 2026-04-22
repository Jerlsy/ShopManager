using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShopManager.Views.ShopSettings;

public partial class LogoCropWindow : Window
{
    private const double CropLeft   = 80;
    private const double CropTop    = 80;
    private const double CropSize   = 240;
    private const double CornerR    = 32;
    private const double CenterX    = 200;
    private const double CenterY    = 200;
    private const int    OutputSize = 240;

    private BitmapSource? _source;
    private double _scale = 1.0;
    private double _offsetX, _offsetY;
    private Point _dragStart;
    private double _dragStartOffsetX, _dragStartOffsetY;
    private bool _isDragging;

    public byte[]? CroppedPng { get; private set; }

    public LogoCropWindow(string imagePath)
    {
        InitializeComponent();
        Loaded += (_, _) => LoadImage(imagePath);
    }

    private void LoadImage(string path)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.UriSource = new Uri(path, UriKind.Absolute);
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.EndInit();
        _source = bi;

        PreviewImage.Source = bi;
        PreviewImage.Width  = bi.PixelWidth;
        PreviewImage.Height = bi.PixelHeight;

        _scale = Math.Max(CropSize / Math.Min(bi.PixelWidth, bi.PixelHeight), 0.05);
        _offsetX = (400 - bi.PixelWidth  * _scale) / 2;
        _offsetY = (400 - bi.PixelHeight * _scale) / 2;
        ApplyTransform();
    }

    private void ApplyTransform()
    {
        ImgScale.ScaleX    = _scale;
        ImgScale.ScaleY    = _scale;
        ImgTranslate.X     = _offsetX;
        ImgTranslate.Y     = _offsetY;
    }

    private void CropCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        _isDragging = true;
        _dragStart = e.GetPosition(CropCanvas);
        _dragStartOffsetX = _offsetX;
        _dragStartOffsetY = _offsetY;
        CropCanvas.CaptureMouse();
    }

    private void CropCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        var pos = e.GetPosition(CropCanvas);
        _offsetX = _dragStartOffsetX + pos.X - _dragStart.X;
        _offsetY = _dragStartOffsetY + pos.Y - _dragStart.Y;
        ApplyTransform();
    }

    private void CropCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        CropCanvas.ReleaseMouseCapture();
    }

    private void CropCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        CropCanvas.ReleaseMouseCapture();
    }

    private void CropCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor   = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
        double newScale = Math.Clamp(_scale * factor, 0.05, 20.0);
        double ratio    = newScale / _scale;
        _offsetX = CenterX - (CenterX - _offsetX) * ratio;
        _offsetY = CenterY - (CenterY - _offsetY) * ratio;
        _scale   = newScale;
        ApplyTransform();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_source == null) { DialogResult = false; return; }
        CroppedPng = RenderRoundedPng();
        DialogResult = true;
    }

    private byte[] RenderRoundedPng()
    {
        double scaleFactor = (double)OutputSize / CropSize;

        double destLeft = (_offsetX - CropLeft) * scaleFactor;
        double destTop  = (_offsetY - CropTop)  * scaleFactor;
        double destW    = _source!.PixelWidth  * _scale * scaleFactor;
        double destH    = _source!.PixelHeight * _scale * scaleFactor;

        double outRadius = CornerR * scaleFactor;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushClip(new RectangleGeometry(
                new Rect(0, 0, OutputSize, OutputSize),
                outRadius, outRadius));
            dc.DrawImage(_source, new Rect(destLeft, destTop, destW, destH));
        }

        var rtb = new RenderTargetBitmap(OutputSize, OutputSize, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);

        using var ms = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        encoder.Save(ms);
        return ms.ToArray();
    }
}
