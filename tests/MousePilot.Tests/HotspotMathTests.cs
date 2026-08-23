using MousePilot.Views;

namespace MousePilot.Tests;

public class HotspotMathTests
{
    [Theory]
    [InlineData(0.0, 0.0, 256.0, 32, 0, 0)]
    [InlineData(255.9, 255.9, 256.0, 32, 31, 31)]   // 右下角 → 最後一格
    [InlineData(128.0, 8.0, 256.0, 32, 16, 1)]      // 每格 8px：128/8=16、8/8=1
    [InlineData(-5.0, 300.0, 256.0, 32, 0, 31)]     // 出界夾制
    public void 顯示座標轉像素(double cx, double cy, double display, int size, int ex, int ey)
    {
        Assert.Equal((ex, ey), HotspotMath.DisplayToPixel(cx, cy, display, size));
    }

    [Theory]
    [InlineData(0, 256.0, 32, 4.0)]    // 第 0 格中心 = 0.5/32*256
    [InlineData(31, 256.0, 32, 252.0)]
    public void 像素轉顯示中心點(int pixel, double display, int size, double expected)
    {
        Assert.Equal(expected, HotspotMath.PixelToDisplayCenter(pixel, display, size), 3);
    }
}
