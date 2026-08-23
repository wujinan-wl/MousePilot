using MousePilot.Models;

namespace MousePilot.Tests;

public class AppSettingsTests
{
    [Fact]
    public void 預設值符合規格()
    {
        var s = new AppSettings();
        Assert.Equal(120, s.IdleStartSeconds);
        Assert.Equal(30, s.MovementIntervalSeconds);
        Assert.Equal(3, s.MovementPixels);
        Assert.Equal(MovementMode.Random, s.MovementMode);
        Assert.True(s.ReturnToOriginalPosition);
        Assert.False(s.RunAtStartup);
        Assert.True(s.StartMinimized);
        Assert.True(s.AutoStartMonitoring);
        Assert.True(s.MinimizeToTrayOnClose);
        Assert.False(s.CustomCursorEnabled);
        Assert.Equal("", s.CursorFile);
        Assert.Equal(32, s.CursorSize);
        Assert.Equal(0, s.CursorHotspotX);
        Assert.Equal(0, s.CursorHotspotY);
        Assert.Equal("Ctrl+Alt+F9", s.ToggleHotkey);
        Assert.Equal("Ctrl+Alt+F10", s.RestoreCursorHotkey);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(4, 5)]
    [InlineData(5, 5)]
    [InlineData(120, 120)]
    [InlineData(86400, 86400)]
    [InlineData(100000, 86400)]
    public void IdleStartSeconds夾制在5到86400(int input, int expected)
    {
        var s = new AppSettings { IdleStartSeconds = input };
        s.Clamp();
        Assert.Equal(expected, s.IdleStartSeconds);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(30, 30)]
    [InlineData(100000, 86400)]
    public void MovementIntervalSeconds夾制在1到86400(int input, int expected)
    {
        var s = new AppSettings { MovementIntervalSeconds = input };
        s.Clamp();
        Assert.Equal(expected, s.MovementIntervalSeconds);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 3)]
    [InlineData(100, 100)]
    [InlineData(500, 100)]
    public void MovementPixels夾制在1到100(int input, int expected)
    {
        var s = new AppSettings { MovementPixels = input };
        s.Clamp();
        Assert.Equal(expected, s.MovementPixels);
    }

    [Theory]
    [InlineData(16, 16)]
    [InlineData(48, 48)]
    [InlineData(33, 32)]   // 非允許尺寸 → 回到預設 32
    [InlineData(-1, 32)]
    public void CursorSize只允許規格清單(int input, int expected)
    {
        var s = new AppSettings { CursorSize = input };
        s.Clamp();
        Assert.Equal(expected, s.CursorSize);
    }

    [Fact]
    public void Clamp夾制Hotspot於尺寸範圍()
    {
        var s = new AppSettings { CursorSize = 32, CursorHotspotX = 999, CursorHotspotY = -5 };
        s.Clamp();
        Assert.Equal(31, s.CursorHotspotX);
        Assert.Equal(0, s.CursorHotspotY);
    }

    [Fact]
    public void Clamp互斥時保留Preset()
    {
        var s = new AppSettings { CursorPreset = "Arrow", CursorFile = @"C:\x.png" };
        s.Clamp();
        Assert.Equal("Arrow", s.CursorPreset);
        Assert.Equal("", s.CursorFile);
    }

    [Fact]
    public void ConfirmedCursorFile預設空並於null時正規化()
    {
        Assert.Equal("", new AppSettings().ConfirmedCursorFile);
        var s = new AppSettings { ConfirmedCursorFile = null! };
        s.Clamp();
        Assert.Equal("", s.ConfirmedCursorFile);
    }
}
