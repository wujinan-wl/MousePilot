using MousePilot.Models;
using MousePilot.Services;

namespace MousePilot.Tests;

public class IdleStateMachineTests
{
    private const int Threshold = 120; // 秒
    private const int Interval = 30;   // 秒

    private static IdleStateMachine Started(uint now = 0, uint lastInput = 0)
    {
        var m = new IdleStateMachine();
        m.Start(now, lastInput);
        return m;
    }

    [Fact]
    public void 初始為已暫停()
    {
        Assert.Equal(MonitorStatus.Paused, new IdleStateMachine().State);
    }

    [Fact]
    public void 暫停時Tick仍回報閒置秒數但狀態維持已暫停()
    {
        var m = new IdleStateMachine();
        var r = m.Tick(10_000, 4_000, Threshold, Interval);
        Assert.Equal(MonitorStatus.Paused, r.State);
        Assert.Equal(6.0, r.IdleSeconds);
        Assert.False(r.MoveRequested);
    }

    [Fact]
    public void 剛有輸入時為使用者活動中()
    {
        var m = Started();
        var r = m.Tick(1_000, 0, Threshold, Interval);
        Assert.Equal(MonitorStatus.UserActive, r.State);
    }

    [Fact]
    public void 閒置累積後為監控中且倒數正確()
    {
        var m = Started();
        var r = m.Tick(30_000, 0, Threshold, Interval);
        Assert.Equal(MonitorStatus.Monitoring, r.State);
        Assert.Equal(30.0, r.IdleSeconds);
        Assert.Equal(90.0, r.SecondsUntilFirstTrigger);
    }

    [Fact]
    public void 達到門檻時觸發第一次移動並進入等待啟動()
    {
        var m = Started();
        var r = m.Tick(120_000, 0, Threshold, Interval);
        Assert.True(r.MoveRequested);
        Assert.Equal(MonitorStatus.WaitingToStart, r.State);
        Assert.Equal(30.0, r.SecondsUntilNextMove);
    }

    [Fact]
    public void 門檻剛好等於也觸發()
    {
        var m = Started();
        var r = m.Tick((uint)Threshold * 1000, 0, Threshold, Interval);
        Assert.True(r.MoveRequested);
    }

    [Fact]
    public void 觸發後依間隔再次觸發()
    {
        var m = Started();
        m.Tick(120_000, 0, Threshold, Interval);            // 第一次觸發
        var r1 = m.Tick(135_000, 0, Threshold, Interval);   // 間隔中
        Assert.False(r1.MoveRequested);
        Assert.Equal(15.0, r1.SecondsUntilNextMove);
        var r2 = m.Tick(150_000, 0, Threshold, Interval);   // 滿 30 秒
        Assert.True(r2.MoveRequested);
    }

    [Fact]
    public void 自動週期中偵測到真實輸入立即取消並重新計時()
    {
        var m = Started();
        m.Tick(120_000, 0, Threshold, Interval);                 // 已觸發、進入週期
        var r = m.Tick(125_000, 124_500, Threshold, Interval);   // 使用者於 124.5s 操作
        Assert.Equal(MonitorStatus.UserActive, r.State);
        Assert.False(r.MoveRequested);
        Assert.Equal(0.5, r.IdleSeconds);
        Assert.Equal(119.5, r.SecondsUntilFirstTrigger);
    }

    [Fact]
    public void 抑制窗內的輸入變化不重置閒置()
    {
        var m = Started();
        m.Tick(60_000, 0, Threshold, Interval);
        m.Suppress(60_000, 1_000);                               // 模擬輸入前宣告抑制窗
        var r = m.Tick(60_500, 60_200, Threshold, Interval);     // OS 記到 60.2s 的模擬輸入
        Assert.Equal(MonitorStatus.Monitoring, r.State);
        Assert.Equal(60.5, r.IdleSeconds);                       // 閒置未被重置
    }

    [Fact]
    public void 抑制窗過期後的輸入仍視為真實使用者()
    {
        var m = Started();
        m.Suppress(60_000, 1_000);                               // 窗 [60000, 61000]
        var r = m.Tick(62_000, 61_800, Threshold, Interval);     // 窗已過期
        Assert.Equal(MonitorStatus.UserActive, r.State);
        Assert.Equal(0.2, r.IdleSeconds, 3);
    }

    [Fact]
    public void Tick計數繞回仍計算正確()
    {
        var m = Started(uint.MaxValue - 10_000, uint.MaxValue - 10_000);
        var r = m.Tick(20_000, uint.MaxValue - 10_000, Threshold, Interval);
        Assert.Equal(MonitorStatus.Monitoring, r.State);
        Assert.Equal(30.001, r.IdleSeconds, 3);
    }

    [Fact]
    public void 暫停後狀態立即為已暫停且再啟動可重新運作()
    {
        var m = Started();
        m.Tick(119_000, 0, Threshold, Interval);
        m.Pause();
        Assert.Equal(MonitorStatus.Paused, m.State);
        m.Start(119_000, 0);
        var r = m.Tick(120_000, 0, Threshold, Interval);
        Assert.True(r.MoveRequested); // OS 閒置基準未變，達門檻即觸發
    }
}
