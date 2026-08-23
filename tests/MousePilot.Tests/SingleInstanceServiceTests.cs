using MousePilot.Services;

namespace MousePilot.Tests;

public class SingleInstanceServiceTests
{
    private static string UniqueName() => "MousePilotTest-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void 第一實例取得成功()
    {
        using var svc = new SingleInstanceService(UniqueName());
        Assert.True(svc.TryAcquire());
    }

    [Fact]
    public void 第二實例取得失敗()
    {
        var name = UniqueName();
        using var first = new SingleInstanceService(name);
        using var second = new SingleInstanceService(name);
        Assert.True(first.TryAcquire());
        Assert.False(second.TryAcquire());
    }

    [Fact]
    public void 喚醒訊號送達第一實例()
    {
        var name = UniqueName();
        using var first = new SingleInstanceService(name);
        using var second = new SingleInstanceService(name);
        Assert.True(first.TryAcquire());
        using var woken = new ManualResetEventSlim(false);
        first.WakeRequested += woken.Set;

        second.SignalFirstInstance();

        Assert.True(woken.Wait(TimeSpan.FromSeconds(5)), "5 秒內未收到喚醒訊號");
    }

    [Fact]
    public void 釋放後可再取得()
    {
        var name = UniqueName();
        var first = new SingleInstanceService(name);
        Assert.True(first.TryAcquire());
        first.Dispose();

        using var next = new SingleInstanceService(name);
        Assert.True(next.TryAcquire());
    }

    [Fact]
    public void 前實例異常結束後可接手()
    {
        var name = UniqueName();
        // 模擬前實例 crash：執行緒持有 mutex 未釋放即結束 → abandoned
        var thread = new Thread(() =>
        {
            var abandoned = new Mutex(false, name);
            abandoned.WaitOne(0);
            // 不 Release、不 Dispose——執行緒結束時 kernel 標記 abandoned
        });
        thread.Start();
        thread.Join();

        using var svc = new SingleInstanceService(name);
        Assert.True(svc.TryAcquire()); // AbandonedMutexException 路徑 → 接手
    }

    [Fact]
    public void 未取得者Dispose不影響持有者()
    {
        var name = UniqueName();
        using var first = new SingleInstanceService(name);
        var second = new SingleInstanceService(name);
        Assert.True(first.TryAcquire());
        Assert.False(second.TryAcquire());

        second.Dispose(); // 未持有者釋放不得誤放第一實例的 mutex

        using var third = new SingleInstanceService(name);
        Assert.False(third.TryAcquire());
    }
}
