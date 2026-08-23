using System.Drawing;
using MousePilot.Views;

namespace MousePilot.Tests;

public class BitmapInteropTests
{
    [Fact]
    public void 轉換保留尺寸且已凍結()
    {
        using var bmp = new Bitmap(7, 5, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        bmp.SetPixel(0, 0, Color.Red);

        var source = BitmapInterop.ToBitmapSource(bmp);

        Assert.Equal(7, source.PixelWidth);
        Assert.Equal(5, source.PixelHeight);
        Assert.True(source.IsFrozen);
    }

    [Fact]
    public void 轉換後與原Bitmap獨立()
    {
        var bmp = new Bitmap(3, 3);
        var source = BitmapInterop.ToBitmapSource(bmp);
        bmp.Dispose(); // 原圖釋放後 BitmapSource 仍可用（OnLoad 已完整載入）

        Assert.Equal(3, source.PixelWidth);
    }
}
