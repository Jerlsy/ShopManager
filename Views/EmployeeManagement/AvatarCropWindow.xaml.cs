using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShopManager.Views.EmployeeManagement;

public partial class AvatarCropWindow : Window
{
    private const double CircleRadius = 120;
    private const double CircleCenterX = 200;
    private const double CircleCenterY = 200;
    private const int OutputSize = 200;

    private BitmapSource? _source;
    private double _scale = 1.0;
    private double _offsetX, _offsetY;
    private Point _dragStart;
    private double _dragStartOffsetX, _dragStartOffsetY;
    private bool _isDragging;

    public byte[]? CroppedPng { get; private set; }

    public AvatarCropWindow(string imagePath)
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
        PreviewImage.Width = bi.PixelWidth;
        PreviewImage.Height = bi.PixelHeight;

        // Initial scale: ensure the image's smaller dimension fills the circle (240px)
        _scale = Math.Max(
            240.0 / Math.Min(bi.PixelWidth, bi.PixelHeight),
            0.05);

        // Center the image in the canvas
        _offsetX = (400 - bi.PixelWidth * _scale) / 2;
        _offsetY = (400 - bi.PixelHeight * _scale) / 2;
        ApplyTransform();
    }

    private void ApplyTransform()
    {
        ImgScale.ScaleX = _scale;
        ImgScale.ScaleY = _scale;
        ImgTranslate.X = _offsetX;
        ImgTranslate.Y = _offsetY;
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
        double factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
        double newScale = Math.Clamp(_scale * factor, 0.05, 20.0);
        double ratio = newScale / _scale;

        // Zoom around canvas center (200, 200)
        _offsetX = CircleCenterX - (CircleCenterX - _offsetX) * ratio;
        _offsetY = CircleCenterY - (CircleCenterY - _offsetY) * ratio;
        _scale = newScale;
        ApplyTransform();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_source == null) { DialogResult = false; return; }
        CroppedPng = RenderCircularPng();
        DialogResult = true;
    }

    private byte[] RenderCircularPng()
    {
        double circleLeft = CircleCenterX - CircleRadius;   // 80
        double circleTop  = CircleCenterY - CircleRadius;   // 80
        double scaleFactor = (double)OutputSize / (CircleRadius * 2); // 200/240

        // Image rect in canvas coords (TransformGroup: Scale then Translate)
        double dispW = _source!.PixelWidth  * _scale;
        double dispH = _source!.PixelHeight * _scale;
        double imgLeft = _offsetX;
        double imgTop  = _offsetY;

        // Map to output coordinate system
        double destLeft = (imgLeft - circleLeft) * scaleFactor;
        double destTop  = (imgTop  - circleTop)  * scaleFactor;
        double destW    = dispW * scaleFactor;
        double destH    = dispH * scaleFactor;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushClip(new EllipseGeometry(
                new Point(OutputSize / 2.0, OutputSize / 2.0),
                OutputSize / 2.0,
                OutputSize / 2.0));
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
