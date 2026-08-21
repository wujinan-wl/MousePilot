using MousePilot.Models;
using MousePilot.Native;

namespace MousePilot.Services;

/// <summary>
/// 執行一次滑鼠微移（含可選返回原位）。所有外部相依可注入以便測試。
/// 單一抑制窗涵蓋整個「移動＋等待＋返回」動作（Phase 2 移交約束 1）；
/// 返回前雙重輸入檢查（lastInput 基準 + 游標位置，移交約束 4）。
/// </summary>
public sealed class MouseMovementService
{
    public const int ReturnDelayMs = 300;      // 規格 §5：100~500ms 取合理預設
    private const int SuppressMarginMs = 200;

    private readonly AppSettings _settings;
    private readonly IdleDetectionService _idleService;
    private readonly Func<(int X, int Y)?> _cursorProvider;
    private readonly Func<ScreenBounds> _boundsProvider;
    private readonly Func<int, int, bool> _sendMove;
    private readonly Func<int, int, bool> _correctPosition;
    private readonly Func<uint> _lastInputProvider;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<int> _randomIndexProvider;
    private readonly Random _random = new();

    private bool _togglePositive = true;

    public MouseMovementService(
        AppSettings settings,
        IdleDetectionService idleService,
        Func<(int X, int Y)?>? cursorProvider = null,
        Func<ScreenBounds>? boundsProvider = null,
        Func<int, int, bool>? sendMove = null,
        Func<int, int, bool>? correctPosition = null,
        Func<uint>? lastInputProvider = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<int>? randomIndexProvider = null)
    {
        _settings = settings;
        _idleService = idleService;
        _cursorProvider = cursorProvider ?? NativeMethods.GetCursorPosition;
        _boundsProvider = boundsProvider ?? NativeMethods.GetVirtualScreenBounds;
        _sendMove = sendMove ?? ((x, y) => NativeMethods.SendMouseMoveAbsolute(x, y, _boundsProvider()));
        _correctPosition = correctPosition ?? NativeMethods.SetCursorPosition;
        _lastInputProvider = lastInputProvider ?? NativeMethods.GetLastInputTick;
        _delay = delay ?? Task.Delay;
        _randomIndexProvider = randomIndexProvider ?? (() => _random.Next(8));
    }

    /// <summary>執行一次移動。true=完整執行；false=被取消或保守放棄（不視為錯誤）。</summary>
    public async Task<bool> ExecuteMoveAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return false; // 已取消（使用者操作中）→ 完全不動作
        }

        if (_cursorProvider() is not { } origin)
        {
            return false; // 讀不到游標→保守放棄（移交約束 5）
        }

        var bounds = _boundsProvider();
        var pixels = Math.Clamp(_settings.MovementPixels, 1, 100);
        var offset = MovementPlanner.NextOffset(_settings.MovementMode, pixels, _togglePositive, _randomIndexProvider());
        _togglePositive = !_togglePositive;
        var target = MovementPlanner.ApplyWithinBounds(origin, offset, bounds);

        // 單一抑制窗涵蓋整個動作（移動＋等待＋返回＋餘裕）
        var returnPhaseMs = _settings.ReturnToOriginalPosition ? ReturnDelayMs : 0;
        _idleService.Suppress(TimeSpan.FromMilliseconds(returnPhaseMs + SuppressMarginMs));

        if (!_sendMove(target.X, target.Y))
        {
            return false;
        }

        if (!_settings.ReturnToOriginalPosition)
        {
            return true;
        }

        var inputAfterMove = _lastInputProvider();
        try
        {
            await _delay(TimeSpan.FromMilliseconds(ReturnDelayMs), ct);
        }
        catch (OperationCanceledException)
        {
            return false; // 使用者操作→立即放棄返回（規格 §24）
        }

        if (ct.IsCancellationRequested)
        {
            return false;
        }

        // 返回前最後防線：等待期間出現新輸入（鍵盤）或游標不在 target（滑鼠被動過）→ 放棄返回
        if (_lastInputProvider() != inputAfterMove)
        {
            return false;
        }

        if (_cursorProvider() is not { } current || current != target)
        {
            return false;
        }

        if (!_sendMove(origin.X, origin.Y))
        {
            return false;
        }

        // 絕對座標正規化可能 ±1px：SetCursorPos 精準校正（不產生輸入事件）
        if (_cursorProvider() is { } after && after != origin)
        {
            _correctPosition(origin.X, origin.Y);
        }

        return true;
    }
}
