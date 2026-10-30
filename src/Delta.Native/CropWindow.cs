using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Delta.Native;
internal sealed class CropWindow : Window
{
    private readonly BitmapSource image; private readonly Canvas canvas = new();
    private readonly Rectangle selection = new() { Stroke = Brushes.DeepSkyBlue, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(30, 20, 120, 255)) };
    private Point? start;
    public byte[]? Result { get; private set; }
    public CropWindow(byte[] png)
    {
        WindowsServices.ProtectWindow(this);
        using var stream = new MemoryStream(png); var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); image = bitmap;
        var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        var dpi = VisualTreeHelper.GetDpi(System.Windows.Application.Current.MainWindow);
        Left = bounds.Left / dpi.DpiScaleX; Top = bounds.Top / dpi.DpiScaleY; Width = bounds.Width / dpi.DpiScaleX; Height = bounds.Height / dpi.DpiScaleY;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; Cursor = Cursors.Cross;
        var grid = new Grid(); grid.Children.Add(new Image { Source = bitmap, Stretch = Stretch.Fill }); grid.Children.Add(canvas); canvas.Background = Brushes.Transparent; canvas.Children.Add(selection); Content = grid;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; Close(); } };
        canvas.MouseLeftButtonDown += (_, e) => { start = e.GetPosition(canvas); canvas.CaptureMouse(); };
        canvas.MouseMove += (_, e) => { if (start is not { } s) return; var p = e.GetPosition(canvas); Canvas.SetLeft(selection, Math.Min(p.X, s.X)); Canvas.SetTop(selection, Math.Min(p.Y, s.Y)); selection.Width = Math.Abs(p.X - s.X); selection.Height = Math.Abs(p.Y - s.Y); };
        canvas.MouseLeftButtonUp += (_, e) => {
            canvas.ReleaseMouseCapture(); if (start == null || selection.Width < 5 || selection.Height < 5) { start = null; return; }
            double sx = image.PixelWidth / canvas.ActualWidth, sy = image.PixelHeight / canvas.ActualHeight;
            int x = Math.Clamp((int)(Canvas.GetLeft(selection) * sx), 0, image.PixelWidth - 1), y = Math.Clamp((int)(Canvas.GetTop(selection) * sy), 0, image.PixelHeight - 1);
            int w = Math.Clamp((int)(selection.Width * sx), 1, image.PixelWidth - x), h = Math.Clamp((int)(selection.Height * sy), 1, image.PixelHeight - y);
            var crop = new CroppedBitmap(image, new Int32Rect(x, y, w, h)); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(crop)); using var output = new MemoryStream(); encoder.Save(output); Result = output.ToArray(); DialogResult = true; Close();
        };
    }
}
