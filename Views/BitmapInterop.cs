using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;

namespace MousePilot.Views;

/// <summary>System.Drawing ↔ WPF 影像橋。</summary>
public static class BitmapInterop
{
    /// <summary>Bitmap → BitmapSource：經 PNG 串流（免 HBitmap 洩漏），OnLoad 完整載入後 Freeze（與來源 Bitmap 完全脫鉤）。</summary>
    public static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = ms;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
