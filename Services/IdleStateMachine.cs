using MousePilot.Models;

namespace MousePilot.Services;

public readonly record struct IdleTickResult(
    MonitorStatus State,
    double IdleSeconds,
    double? SecondsUntilFirstTrigger,
    double? SecondsUntilNextMove,
    bool MoveRequested);

/// <summary>
/// 閒置偵測純狀態機：不含 timer、不呼叫 Win32，由呼叫端每次 Tick 餵入目前 tick
/// 與 OS 最後輸入 tick（毫秒、32-bit，繞回以 unchecked 差值處理）。
/// 抑制窗內發生的輸入變化視為程式自身模擬輸入、不重置閒置計時
/// （Spike A 實測：SendInput 會重置 GetLastInputInfo）。
/// </summary>
public sealed class IdleStateMachine
{
    private const uint UserActiveWindowMs = 2000;

    private bool _running;
    private uint _lastRealInputTick;
    private uint _suppressStartTick;
    private uint _suppressUntilTick;
    private bool _suppressActive;
    private bool _autoCycleActive;
    private uint _lastMoveTick;

    public MonitorStatus State { get; private set; } = MonitorStatus.Paused;

    public void Start(uint nowTick, uint lastInputTick)
    {
        _running = true;
        _lastRealInputTick = lastInputTick;
        _autoCycleActive = false;
        State = MonitorStatus.Monitoring;
    }

    public void Pause()
    {
        _running = false;
        _autoCycleActive = false;
        _suppressActive = false;
        State = MonitorStatus.Paused;
    }

    /// <summary>Phase 3 送出模擬輸入前呼叫：nowTick 起 durationMs 內的輸入變化不視為使用者操作。</summary>
    public void Suppress(uint nowTick, uint durationMs)
    {
        _suppressActive = true;
        _suppressStartTick = nowTick;
        _suppressUntilTick = unchecked(nowTick + durationMs);
    }

    public IdleTickResult Tick(uint nowTick, uint lastInputTick, int idleStartSeconds, int intervalSeconds)
    {
        if (!_running)
        {
            return new IdleTickResult(
                MonitorStatus.Paused, unchecked(nowTick - lastInputTick) / 1000.0, null, null, false);
        }

        if (lastInputTick != _lastRealInputTick)
        {
            // 分類只看「值域」：lastInputTick 是否落在抑制窗範圍內。
            // 不可用 nowTick 對窗做時間過期判斷——輪詢常落在窗結束之後，
            // 窗內殘留的模擬輸入 tick 會被誤判為真實輸入（review 實測情境）。
            // 舊窗的存留期在實務上由 Phase 3 每次移動前重新 Suppress 所取代，
            // 且採納真實輸入時即作廢，不會存活到 24.8 天的繞回混疊範圍。
            var inWindow = _suppressActive
                && (int)unchecked(lastInputTick - _suppressStartTick) >= 0
                && (int)unchecked(_suppressUntilTick - lastInputTick) >= 0;
            if (!inWindow)
            {
                // 真實使用者輸入：取消自動週期、重新計時（規格 §6/§24 最高優先）
                _lastRealInputTick = lastInputTick;
                _autoCycleActive = false;
                _suppressActive = false; // 真實輸入採納後，舊抑制窗作廢
            }
            // 抑制窗內：不採納，閒置基準維持 _lastRealInputTick
        }

        var idleMs = unchecked(nowTick - _lastRealInputTick);
        var idleSeconds = idleMs / 1000.0;
        var thresholdMs = (uint)idleStartSeconds * 1000u;
        var intervalMs = (uint)intervalSeconds * 1000u;

        if (!_autoCycleActive)
        {
            if (idleMs >= thresholdMs)
            {
                _autoCycleActive = true;
                _lastMoveTick = nowTick;
                State = MonitorStatus.WaitingToStart;
                // 已進入自動週期：第一次觸發倒數改為 null（UI 顯示「—」），只剩下一次移動倒數
                return new IdleTickResult(State, idleSeconds, null, intervalMs / 1000.0, true);
            }

            State = idleMs < UserActiveWindowMs ? MonitorStatus.UserActive : MonitorStatus.Monitoring;
            return new IdleTickResult(State, idleSeconds, (thresholdMs - idleMs) / 1000.0, null, false);
        }

        var sinceMove = unchecked(nowTick - _lastMoveTick);
        var moveRequested = false;
        if (sinceMove >= intervalMs)
        {
            moveRequested = true;
            _lastMoveTick = nowTick;
            sinceMove = 0;
        }

        State = MonitorStatus.WaitingToStart;
        return new IdleTickResult(State, idleSeconds, null, (intervalMs - sinceMove) / 1000.0, moveRequested);
    }
}
