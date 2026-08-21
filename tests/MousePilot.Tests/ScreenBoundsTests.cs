using MousePilot.Models;

namespace MousePilot.Tests;

public class ScreenBoundsTests
{
    [Fact]
    public void Right與Bottom為含端點邊界()
    {
        var b = new ScreenBounds(0, 0, 1920, 1080);
        Assert.Equal(1919, b.Right);
        Assert.Equal(1079, b.Bottom);
    }

    [Fact]
    public void Contains判斷內部與邊界點()
    {
        var b = new ScreenBounds(0, 0, 1920, 1080);
        Assert.True(b.Contains(0, 0));
        Assert.True(b.Contains(1919, 1079));
        Assert.False(b.Contains(1920, 0));
        Assert.False(b.Contains(0, -1));
    }

    [Fact]
    public void 負座標螢幕Contains正確()
    {
        var b = new ScreenBounds(-1920, -500, 3840, 1580); // 左側+上方延伸的虛擬桌面
        Assert.True(b.Contains(-1920, -500));
        Assert.True(b.Contains(1919, 1079));
        Assert.False(b.Contains(-1921, 0));
    }

    [Fact]
    public void ToAbsolute正規化為0到65535()
    {
        var b = new ScreenBounds(0, 0, 1920, 1080);
        Assert.Equal((0, 0), b.ToAbsolute(0, 0));
        Assert.Equal((65535, 65535), b.ToAbsolute(1919, 1079));
        Assert.Equal(32785, b.ToAbsolute(960, 0).Nx); // round(960*65535/1919) = round(32784.575)
    }

    [Fact]
    public void 負座標原點ToAbsolute為0()
    {
        var b = new ScreenBounds(-1920, 0, 3840, 1080);
        Assert.Equal(0, b.ToAbsolute(-1920, 0).Nx);
        Assert.Equal(65535, b.ToAbsolute(1919, 0).Nx);
    }
}
