using MousePilot.Models;
using MousePilot.Services;

namespace MousePilot.Tests;

public class MouseMovementServiceTests
{
    private sealed class Harness
    {
        public AppSettings Settings { get; } = new() { MovementMode = MovementMode.Horizontal, MovementPixels = 3, ReturnToOriginalPosition = true };
        public (int X, int Y)? Cursor = (500, 300);
        public uint Now;
        public uint LastInput;
        public List<(int X, int Y)> Sent { get; } = new();
        public List<(int X, int Y)> Corrected { get; } = new();
        public int DelayCalls;
        public IdleDetectionService Idle { get; }
        public MouseMovementService Service { get; }

        public Harness(Action<Harness>? beforeReturn = null)
        {
            Idle = new IdleDetectionService(Settings, () => Now, () => LastInput, () => Cursor);
            Service = new MouseMovementService(
                Settings, Idle,
                cursorProvider: () => Cursor,
                boundsProvider: () => new ScreenBounds(0, 0, 1920, 1080),
                sendMove: (x, y) => { Sent.Add((x, y)); Cursor = (x, y); return true; },
                correctPosition: (x, y) => { Corrected.Add((x, y)); Cursor = (x, y); return true; },
                lastInputProvider: () => LastInput,
                delay: (_, ct) => { DelayCalls++; ct.ThrowIfCancellationRequested(); beforeReturn?.Invoke(this); return Task.CompletedTask; },
                randomIndexProvider: () => 3); // 右
        }
    }

    [Fact]
    public async Task 左右模式往返移動並回原位()
    {
        var h = new Harness();
        Assert.True(await h.Service.ExecuteMoveAsync(CancellationToken.None));
        Assert.Equal(new[] { (503, 300), (500, 300) }, h.Sent); // +3 → 回原位
        Assert.Equal(1, h.DelayCalls);

        h.Sent.Clear();
        Assert.True(await h.Service.ExecuteMoveAsync(CancellationToken.None));
        Assert.Equal((497, 300), h.Sent[0]); // 往返：第二次 -3
    }

    [Fact]
    public async Task 不回原位時只送一次()
    {
        var h = new Harness();
        h.Settings.ReturnToOriginalPosition = false;
        Assert.True(await h.Service.ExecuteMoveAsync(CancellationToken.None));
        Assert.Single(h.Sent);
        Assert.Equal(0, h.DelayCalls);
    }

    [Fact]
    public async Task 游標讀取失敗時放棄且不送出()
    {
        var h = new Harness { Cursor = null };
        Assert.False(await h.Service.ExecuteMoveAsync(CancellationToken.None));
        Assert.Empty(h.Sent);
    }

    [Fact]
    public async Task 右緣時自動反向()
    {
        var h = new Harness();
        h.Cursor = (1919, 300);
        await h.Service.ExecuteMoveAsync(CancellationToken.None);
        Assert.Equal((1916, 300), h.Sent[0]);
    }

    [Fact]
    public async Task 已取消的Token完全不動作()
    {
        var h = new Harness();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.False(await h.Service.ExecuteMoveAsync(cts.Token));
        Assert.Empty(h.Sent); // 入口即檢查，去程也不送
    }

    [Fact]
    public async Task 等待返回期間取消時放棄返回()
    {
        using var cts = new CancellationTokenSource();
        var h = new Harness(beforeReturn: _ => cts.Cancel()); // 等待期間被取消（真實輸入路徑）
        Assert.False(await h.Service.ExecuteMoveAsync(cts.Token));
        Assert.Single(h.Sent);                                 // 只送了去程，未返回
    }

    [Fact]
    public async Task 返回前偵測到新輸入時放棄返回()
    {
        var h = new Harness(beforeReturn: x => x.LastInput = 999); // 等待期間出現鍵盤輸入
        Assert.False(await h.Service.ExecuteMoveAsync(CancellationToken.None));
        Assert.Single(h.Sent);
    }

    [Fact]
    public async Task 返回前游標被移動時放棄返回()
    {
        var h = new Harness(beforeReturn: x => x.Cursor = (900, 900)); // 使用者動了滑鼠
        Assert.False(await h.Service.ExecuteMoveAsync(CancellationToken.None));
        Assert.Single(h.Sent);
    }

    [Fact]
    public async Task 抑制窗涵蓋整個動作_模擬輸入不重置閒置()
    {
        var h = new Harness();
        h.Idle.Start();                                          // Now=0, LastInput=0
        await h.Service.ExecuteMoveAsync(CancellationToken.None); // Suppress 於 Now=0，窗 [0, 500]
        h.Now = 10_000;
        h.LastInput = 100;      // 動作期間的（模擬）輸入 tick，落在窗內
        h.Idle.PollNow();
        Assert.Equal(MonitorStatus.Monitoring, h.Idle.State);    // 閒置 10s 未被重置、未誤判為真實輸入
    }

    [Fact]
    public async Task 隨機模式目標在範圍內且距離正確()
    {
        var h = new Harness();
        h.Settings.MovementMode = MovementMode.Random;
        h.Settings.MovementPixels = 5;
        await h.Service.ExecuteMoveAsync(CancellationToken.None);
        Assert.Equal((505, 300), h.Sent[0]); // randomIndex=3 → 右
    }
}
