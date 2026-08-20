using MousePilot.Models;
using MousePilot.Services;

namespace MousePilot.Tests;

public class IdleDetectionServiceTests
{
    [Fact]
    public void PollNow以注入時間來源驅動並發出Ticked事件()
    {
        uint now = 0;
        var settings = new AppSettings { IdleStartSeconds = 5, MovementIntervalSeconds = 30 };
        using var service = new IdleDetectionService(settings, () => now, () => 0u, () => (12, 34));
        IdleTickResult? seen = null;
        (int X, int Y)? pos = null;
        service.Ticked += (r, p) => { seen = r; pos = p; };

        service.Start();
        now = 3_000;
        service.PollNow();

        Assert.Equal(MonitorStatus.Monitoring, seen!.Value.State);
        Assert.Equal(3.0, seen.Value.IdleSeconds);
        Assert.Equal((12, 34), pos);
    }

    [Fact]
    public void 達門檻時發出MoveRequested事件()
    {
        uint now = 0;
        var settings = new AppSettings { IdleStartSeconds = 5 };
        using var service = new IdleDetectionService(settings, () => now, () => 0u, () => (0, 0));
        var moves = 0;
        service.MoveRequested += () => moves++;

        service.Start();
        now = 5_000;
        service.PollNow();

        Assert.Equal(1, moves);
        Assert.Equal(MonitorStatus.WaitingToStart, service.State);
    }

    [Fact]
    public void Suppress期間輸入變化不重置閒置()
    {
        uint now = 10_000;
        uint lastInput = 0;
        var settings = new AppSettings { IdleStartSeconds = 120 };
        using var service = new IdleDetectionService(settings, () => now, () => lastInput, () => (0, 0));
        IdleTickResult? seen = null;
        service.Ticked += (r, _) => seen = r;

        service.Start();
        service.Suppress(TimeSpan.FromMilliseconds(500));
        now = 10_400;
        lastInput = 10_200; // 抑制窗內的（模擬）輸入
        service.PollNow();

        Assert.Equal(10.4, seen!.Value.IdleSeconds);
        Assert.Equal(MonitorStatus.Monitoring, seen.Value.State);
    }
}
