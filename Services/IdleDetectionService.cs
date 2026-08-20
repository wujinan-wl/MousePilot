using System.Windows.Threading;
using MousePilot.Models;
using MousePilot.Native;

namespace MousePilot.Services;

/// <summary>
/// IdleStateMachine 的執行時包裝：DispatcherTimer 每 500ms 輪詢 GetLastInputInfo
/// 與滑鼠座標（規格 §19：500~1000ms、不用 hook）。時間/座標來源可注入以便單元測試
/// （測試中 DispatcherTimer 不會運轉，由 PollNow() 手動驅動）。
/// </summary>
public sealed class IdleDetectionService : IDisposable
{
    private readonly IdleStateMachine _machine = new();
    private readonly AppSettings _settings;
    private readonly Func<uint> _tickProvider;
    private readonly Func<uint> _lastInputProvider;
    private readonly Func<(int X, int Y)> _cursorProvider;
    private readonly DispatcherTimer _timer;

    public event Action<IdleTickResult, (int X, int Y)>? Ticked;
    public event Action? MoveRequested;

    public MonitorStatus State => _machine.State;

    public IdleDetectionService(
        AppSettings settings,
        Func<uint>? tickProvider = null,
        Func<uint>? lastInputProvider = null,
        Func<(int X, int Y)>? cursorProvider = null)
    {
        _settings = settings;
        _tickProvider = tickProvider ?? (() => (uint)Environment.TickCount);
        _lastInputProvider = lastInputProvider ?? NativeMethods.GetLastInputTick;
        _cursorProvider = cursorProvider ?? NativeMethods.GetCursorPosition;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => PollNow();
        _timer.Start();
    }

    public void Start()
    {
        _machine.Start(_tickProvider(), _lastInputProvider());
        PollNow();
    }

    public void Pause()
    {
        _machine.Pause();
        PollNow();
    }

    /// <summary>Phase 3 送出模擬輸入前呼叫，抑制窗內的輸入變化不視為使用者操作。</summary>
    public void Suppress(TimeSpan duration)
        => _machine.Suppress(_tickProvider(), (uint)duration.TotalMilliseconds);

    public void PollNow()
    {
        // 就地夾制：TextBox 綁定會把未夾制的值直接寫進 Settings（AppSettings 無 INPC），
        // 若不夾制，IdleStartSeconds=0 會造成每 500ms 觸發一次（final review Issue 1）
        var result = _machine.Tick(
            _tickProvider(), _lastInputProvider(),
            Math.Clamp(_settings.IdleStartSeconds, 5, 86400),
            Math.Clamp(_settings.MovementIntervalSeconds, 1, 86400));
        Ticked?.Invoke(result, _cursorProvider());
        if (result.MoveRequested)
        {
            MoveRequested?.Invoke();
        }
    }

    public void Dispose() => _timer.Stop();
}
