using MousePilot.Models;
using MousePilot.Services;

namespace MousePilot.Tests;

public class TrayIconServiceTests
{
    private static TrayIconService Create() => new(visible: false);

    [Fact]
    public void 選單結構符合規格順序()
    {
        using var tray = Create();
        Assert.Equal(
            new[]
            {
                "開啟 MousePilot", "-",
                "啟動", "暫停", "立即執行一次", "-",
                "啟用自訂游標", "停用自訂游標", "恢復 Windows 游標", "-",
                "設定", "結束",
            },
            tray.MenuTexts);
    }

    [Fact]
    public void 游標三項於Phase9前停用()
    {
        using var tray = Create();
        Assert.True(tray.FindMenuItem("啟用自訂游標")!.Enabled);
        Assert.True(tray.FindMenuItem("停用自訂游標")!.Enabled);
        Assert.True(tray.FindMenuItem("恢復 Windows 游標")!.Enabled);
    }

    [Fact]
    public void 游標選單項啟用且觸發事件()
    {
        using var tray = new TrayIconService();
        var events = new List<string>();
        tray.EnableCursorRequested += () => events.Add("enable");
        tray.DisableCursorRequested += () => events.Add("disable");
        tray.RestoreCursorRequested += () => events.Add("restore");

        Assert.True(tray.FindMenuItem("啟用自訂游標")!.Enabled);
        tray.FindMenuItem("啟用自訂游標")!.PerformClick();
        tray.FindMenuItem("停用自訂游標")!.PerformClick();
        tray.FindMenuItem("恢復 Windows 游標")!.PerformClick();

        Assert.Equal(new[] { "enable", "disable", "restore" }, events);
    }

    [Fact]
    public void UpdateStatus切換啟動暫停可用性()
    {
        using var tray = Create();
        tray.UpdateStatus(MonitorStatus.Paused, "已暫停");
        Assert.True(tray.FindMenuItem("啟動")!.Enabled);
        Assert.False(tray.FindMenuItem("暫停")!.Enabled);

        tray.UpdateStatus(MonitorStatus.Monitoring, "監控中");
        Assert.False(tray.FindMenuItem("啟動")!.Enabled);
        Assert.True(tray.FindMenuItem("暫停")!.Enabled);
    }

    [Fact]
    public void 選單點擊觸發對應事件()
    {
        using var tray = Create();
        var log = new List<string>();
        tray.OpenRequested += () => log.Add("open");
        tray.StartRequested += () => log.Add("start");
        tray.PauseRequested += () => log.Add("pause");
        tray.MoveOnceRequested += () => log.Add("move");
        tray.ExitRequested += () => log.Add("exit");

        tray.FindMenuItem("開啟 MousePilot")!.PerformClick();
        tray.FindMenuItem("啟動")!.PerformClick();
        tray.FindMenuItem("暫停")!.PerformClick();
        tray.FindMenuItem("立即執行一次")!.PerformClick();
        tray.FindMenuItem("設定")!.PerformClick();   // 設定 → 開 Dashboard
        tray.FindMenuItem("結束")!.PerformClick();

        Assert.Equal(new[] { "open", "start", "pause", "move", "open", "exit" }, log);
    }

    [Fact]
    public void Tooltip過長時截斷為63字元()
    {
        using var tray = Create();
        tray.UpdateStatus(MonitorStatus.Monitoring, new string('監', 100));
        Assert.Equal(63, tray.TooltipText.Length);
        Assert.StartsWith("MousePilot", tray.TooltipText);
    }
}
