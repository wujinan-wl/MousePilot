using MousePilot.Models;
using MousePilot.Services;

namespace MousePilot.Tests;

public class MovementPlannerTests
{
    private static readonly ScreenBounds Screen = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData(true, 3, 0)]
    [InlineData(false, -3, 0)]
    public void 左右模式往返(bool toggle, int dx, int dy)
    {
        Assert.Equal((dx, dy), MovementPlanner.NextOffset(MovementMode.Horizontal, 3, toggle, 0));
    }

    [Theory]
    [InlineData(true, 0, 3)]
    [InlineData(false, 0, -3)]
    public void 上下模式往返(bool toggle, int dx, int dy)
    {
        Assert.Equal((dx, dy), MovementPlanner.NextOffset(MovementMode.Vertical, 3, toggle, 0));
    }

    [Theory]
    [InlineData(0, 0, -5)]   // 上
    [InlineData(1, 0, 5)]    // 下
    [InlineData(2, -5, 0)]   // 左
    [InlineData(3, 5, 0)]    // 右
    [InlineData(4, -5, -5)]  // 左上
    [InlineData(5, 5, -5)]   // 右上
    [InlineData(6, -5, 5)]   // 左下
    [InlineData(7, 5, 5)]    // 右下
    public void 隨機模式八方向距離由像素控制(int index, int dx, int dy)
    {
        Assert.Equal((dx, dy), MovementPlanner.NextOffset(MovementMode.Random, 5, true, index));
    }

    [Fact]
    public void 範圍內位移直接套用()
    {
        Assert.Equal((503, 300), MovementPlanner.ApplyWithinBounds((500, 300), (3, 0), Screen));
    }

    [Fact]
    public void 右緣越界自動反向()
    {
        Assert.Equal((1916, 300), MovementPlanner.ApplyWithinBounds((1919, 300), (3, 0), Screen));
    }

    [Fact]
    public void 負座標螢幕左緣反向()
    {
        var multi = new ScreenBounds(-1920, 0, 3840, 1080);
        Assert.Equal((-1917, 300), MovementPlanner.ApplyWithinBounds((-1920, 300), (-3, 0), multi));
    }

    [Fact]
    public void 角落雙軸同時反向()
    {
        Assert.Equal((1916, 1076), MovementPlanner.ApplyWithinBounds((1919, 1079), (3, 3), Screen));
    }

    [Fact]
    public void 反向仍越界時夾在邊界()
    {
        var tiny = new ScreenBounds(0, 0, 50, 50);
        Assert.Equal((0, 25), MovementPlanner.ApplyWithinBounds((10, 25), (100, 0), tiny)); // 10-100=-90 → 夾 0
    }
}
