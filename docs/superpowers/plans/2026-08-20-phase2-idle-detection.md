# MousePilot Phase 2：Idle Detection 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 完成閒置偵測：`GetLastInputInfo` + 500ms 輪詢的狀態機（含 Phase 3 需要的模擬輸入抑制窗），UI 即時顯示狀態/閒置秒數/倒數/滑鼠座標，啟動/暫停按鈕與「啟動後自動開始監控」生效。

**Architecture:** 三層拆分讓核心邏輯可完全單元測試：`IdleStateMachine`（純狀態機，不含 timer、不碰 Win32，時間全部由參數餵入）→ `IdleDetectionService`（DispatcherTimer 500ms 輪詢包裝，時間/座標來源可注入）→ `MainViewModel`（事件綁定 UI）。PInvoke 集中新增於 `Native/NativeMethods.cs`。本階段觸發點只發佔位事件（觸發次數 +1），實際滑鼠移動屬 Phase 3。

**Tech Stack:** 既有（.NET 8 / WPF / CommunityToolkit.Mvvm / xUnit），無新相依。

**Spec:** `docs/spec/mousepilot-spec.md`（§2、§3、§6、§19、§24、§28、§34 Idle 1~5）；設計依據：`docs/superpowers/research/2026-08-20-spike-findings.md`（Spike A：SendInput 會重置 GetLastInputInfo）；總體計畫：`docs/superpowers/plans/2026-08-20-mousepilot-master-plan.md`。

## 本階段範圍 / 不在範圍

- **範圍：** 狀態機（監控中/使用者活動中/等待啟動/自動移動中/已暫停）、抑制窗 API（Phase 3 用）、tick 繞回處理、UI 狀態區完成（顏色點派生、倒數、座標、觸發次數）、Start/Pause 命令生效、AutoStartMonitoring 生效。
- **不在範圍：** 實際滑鼠移動與 SendInput（Phase 3）、`AutoMoving` 狀態的進入轉移（enum 值先定義，Phase 3 設定）、Tray、Hotkey、AppSettings INPC 化（留待 Phase 6 需要程式改設定時）。

## Global Constraints

- 輪詢用單一 `DispatcherTimer`，間隔 **500ms**（規格 §19 允許 500~1000ms）；禁止 keyboard/mouse hook、禁止 busy loop。
- 所有 tick 計算單位毫秒、32-bit `uint`，差值一律 `unchecked` 處理 49.7 天繞回。
- 狀態文字對應：Paused=已暫停、Monitoring=監控中、UserActive=使用者活動中、WaitingToStart=等待啟動、AutoMoving=自動移動中。顏色（既有資源）：綠 `StatusRunningBrush`（Monitoring/UserActive）、黃 `StatusWaitingBrush`（WaitingToStart/AutoMoving）、灰 `StatusPausedBrush`（Paused）。
- 「真實使用者輸入立即取消自動週期並重新計時」為最高優先（規格 §6/§24）；抑制窗內的輸入變化視為程式自身輸入、不重置（Spike A 結論）；Win32 呼叫失敗時採保守值（視為剛有輸入 → 不觸發移動，規格 §40-1）。
- 無新 NuGet 相依；PInvoke 只放 `Native/NativeMethods.cs`。
- TDD：先測試（RED）後實作（GREEN），RED/GREEN 輸出留存報告。
- Commit 訊息一律寫入暫存檔後 `git commit -F <檔案>`（**禁用 PowerShell here-string**，曾發生 `@` 洩漏）：繁體中文 + 前綴，結尾 `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`，commit 後以 `git log -1 --format=%B` 驗證乾淨。
- 既有測試（31 個）在每個 task 結束時必須全綠（Task 5 依計畫改寫 MainViewModelTests 除外）。

---

### Task 1: MonitorStatus 搬到 Models + StatusText 派生

**Files:**
- Create: `Models/MonitorStatus.cs`
- Modify: `ViewModels/MainViewModel.cs`（移除 enum 定義、新增 `OnStatusChanged` 派生）
- Modify: `tests/MousePilot.Tests/MainViewModelTests.cs`（補 using、新增派生測試）

**Interfaces:**
- Consumes: 既有 `MainViewModel`（`[ObservableProperty] MonitorStatus _status`、`string _statusText`）。
- Produces: `enum MousePilot.Models.MonitorStatus { Paused, Monitoring, UserActive, WaitingToStart, AutoMoving }`（Task 3/4/5 全部引用此 namespace）；`MainViewModel.StatusText` 由 `Status` 自動派生（設 `Status` 即更新 `StatusText`）。

- [ ] **Step 1: 寫失敗測試**

在 `tests/MousePilot.Tests/MainViewModelTests.cs` 的 using 區塊加入 `using MousePilot.Models;`，並在 class 內新增：

```csharp
    [Theory]
    [InlineData(MonitorStatus.Paused, "已暫停")]
    [InlineData(MonitorStatus.Monitoring, "監控中")]
    [InlineData(MonitorStatus.UserActive, "使用者活動中")]
    [InlineData(MonitorStatus.WaitingToStart, "等待啟動")]
    [InlineData(MonitorStatus.AutoMoving, "自動移動中")]
    public void StatusText由Status派生(MonitorStatus status, string expected)
    {
        var vm = new MainViewModel(new SettingsService(SettingsPath));
        vm.Status = status;
        Assert.Equal(expected, vm.StatusText);
    }
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 編譯失敗（`MonitorStatus` 尚在 `MousePilot.ViewModels`，`MousePilot.Models` 中不存在）或新測試 FAIL（StatusText 未派生）。

- [ ] **Step 3: 實作**

建立 `Models/MonitorStatus.cs`：

```csharp
namespace MousePilot.Models;

public enum MonitorStatus
{
    Paused,
    Monitoring,
    UserActive,
    WaitingToStart,
    AutoMoving,
}
```

修改 `ViewModels/MainViewModel.cs`：
1. 刪除檔內的 `public enum MonitorStatus {...}` 定義（`using MousePilot.Models;` 已存在，型別改由 Models 提供）。
2. 在 class 內新增派生（放在 `SaveSettings()` 之前）：

```csharp
    partial void OnStatusChanged(MonitorStatus value) => StatusText = value switch
    {
        MonitorStatus.Paused => "已暫停",
        MonitorStatus.Monitoring => "監控中",
        MonitorStatus.UserActive => "使用者活動中",
        MonitorStatus.WaitingToStart => "等待啟動",
        MonitorStatus.AutoMoving => "自動移動中",
        _ => value.ToString(),
    };
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 全綠（31 + 5 = 36）。

- [ ] **Step 5: Commit**

訊息（寫入暫存檔後 `git commit -F`）：

```text
refactor: MonitorStatus 移至 Models 並由 Status 派生 StatusText

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

`git add Models/MonitorStatus.cs ViewModels/MainViewModel.cs tests/MousePilot.Tests/MainViewModelTests.cs`，commit 後 `git log -1 --format=%B` 驗證乾淨。

---

### Task 2: IdleStateMachine（TDD，本階段核心）

**Files:**
- Create: `Services/IdleStateMachine.cs`
- Test: `tests/MousePilot.Tests/IdleStateMachineTests.cs`

**Interfaces:**
- Consumes: `MousePilot.Models.MonitorStatus`（Task 1）。
- Produces（Task 3/4 依賴，簽章逐字）:
  - `readonly record struct IdleTickResult(MonitorStatus State, double IdleSeconds, double? SecondsUntilFirstTrigger, double? SecondsUntilNextMove, bool MoveRequested)`
  - `sealed class IdleStateMachine`：`MonitorStatus State { get; }`、`void Start(uint nowTick, uint lastInputTick)`、`void Pause()`、`void Suppress(uint nowTick, uint durationMs)`、`IdleTickResult Tick(uint nowTick, uint lastInputTick, int idleStartSeconds, int intervalSeconds)`

- [ ] **Step 1: 寫失敗測試**

`tests/MousePilot.Tests/IdleStateMachineTests.cs`：

```csharp
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
    public void 抑制窗範圍外的輸入仍視為真實使用者()
    {
        var m = Started();
        m.Suppress(60_000, 1_000);                               // 窗 [60000, 61000]
        var r = m.Tick(62_000, 61_800, Threshold, Interval);     // 輸入 tick 在窗範圍之後
        Assert.Equal(MonitorStatus.UserActive, r.State);
        Assert.Equal(0.2, r.IdleSeconds, 3);
    }

    [Fact]
    public void 輪詢落在窗過期後時窗內殘留模擬輸入不被誤判為真實輸入()
    {
        var m = Started();
        m.Tick(120_000, 0, Threshold, Interval);                 // 已觸發，進入自動週期
        m.Suppress(120_000, 50);                                 // 模擬輸入前宣告 50ms 窗
        var r = m.Tick(121_000, 120_010, Threshold, Interval);   // 模擬輸入記在窗內、輪詢在窗後
        Assert.Equal(MonitorStatus.WaitingToStart, r.State);     // 自動週期未被誤取消
        Assert.Equal(121.0, r.IdleSeconds);                      // 閒置未被重置
        Assert.False(r.MoveRequested);
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
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 編譯失敗（`IdleStateMachine` / `IdleTickResult` 不存在）。

- [ ] **Step 3: 實作 Services/IdleStateMachine.cs**

```csharp
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
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 全綠（36 + 13 = 49）。

- [ ] **Step 5: Commit**

```text
feat: IdleStateMachine - 閒置狀態機與模擬輸入抑制窗

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

`git add Services/IdleStateMachine.cs tests/MousePilot.Tests/IdleStateMachineTests.cs`，`-F` 提交後驗證訊息。

---

### Task 3: NativeMethods + IdleDetectionService

**Files:**
- Create: `Native/NativeMethods.cs`
- Create: `Services/IdleDetectionService.cs`
- Test: `tests/MousePilot.Tests/IdleDetectionServiceTests.cs`

**Interfaces:**
- Consumes: `IdleStateMachine` / `IdleTickResult`（Task 2）、`AppSettings`。
- Produces（Task 4 依賴，簽章逐字）:
  - `internal static class MousePilot.Native.NativeMethods`：`internal static uint GetLastInputTick()`、`internal static (int X, int Y) GetCursorPosition()`
  - `sealed class IdleDetectionService : IDisposable`：建構子 `IdleDetectionService(AppSettings settings, Func<uint>? tickProvider = null, Func<uint>? lastInputProvider = null, Func<(int X, int Y)>? cursorProvider = null)`；`event Action<IdleTickResult, (int X, int Y)>? Ticked`；`event Action? MoveRequested`；`MonitorStatus State { get; }`；`void Start()`；`void Pause()`；`void Suppress(TimeSpan duration)`；`void PollNow()`；`void Dispose()`

- [ ] **Step 1: 寫失敗測試**

`tests/MousePilot.Tests/IdleDetectionServiceTests.cs`：

```csharp
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
    public void 設定為0時不會每次輪詢都觸發()
    {
        uint now = 0;
        var settings = new AppSettings();
        settings.IdleStartSeconds = 0;          // 模擬 UI 未夾制輸入直接寫入
        settings.MovementIntervalSeconds = 0;
        using var service = new IdleDetectionService(settings, () => now, () => 0u, () => (0, 0));
        var moves = 0;
        service.MoveRequested += () => moves++;

        service.Start();
        now = 2_000;
        service.PollNow();
        Assert.Equal(0, moves);                 // 夾制後門檻至少 5 秒

        now = 5_000;
        service.PollNow();
        Assert.Equal(1, moves);                 // 夾制後門檻 5 秒 → 觸發一次

        now = 5_500;
        service.PollNow();
        Assert.Equal(1, moves);                 // 夾制後間隔至少 1 秒 → 0.5 秒不再觸發
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
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 編譯失敗（`IdleDetectionService` 不存在）。

- [ ] **Step 3: 實作**

`Native/NativeMethods.cs`：

```csharp
using System.Runtime.InteropServices;

namespace MousePilot.Native;

internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    /// <summary>最後一次輸入的 tick（毫秒）。失敗時回傳目前 tick——視為「剛有輸入」的保守值，確保不誤觸發移動（規格 §40-1）。</summary>
    internal static uint GetLastInputTick()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info) ? info.dwTime : (uint)Environment.TickCount;
    }

    internal static (int X, int Y) GetCursorPosition()
        => GetCursorPos(out var p) ? (p.X, p.Y) : (0, 0);
}
```

`Services/IdleDetectionService.cs`：

```csharp
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
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 全綠（49 + 3 = 52）。

注意：`Start()` 內含 `PollNow()`，測試「達門檻時發出MoveRequested事件」在 `Start()` 當下 now=0、idle=0 不會觸發，改 now=5000 後的 `PollNow()` 才觸發——如實驗證此順序。

- [ ] **Step 5: Commit**

```text
feat: IdleDetectionService 與 NativeMethods - 500ms 輪詢包裝

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

`git add Native/NativeMethods.cs Services/IdleDetectionService.cs tests/MousePilot.Tests/IdleDetectionServiceTests.cs`，`-F` 提交後驗證訊息。

---

### Task 4: MainViewModel 整合（TDD）

**Files:**
- Modify: `ViewModels/MainViewModel.cs`（整檔覆寫為下方內容）
- Modify: `tests/MousePilot.Tests/MainViewModelTests.cs`（整檔覆寫為下方內容）

**Interfaces:**
- Consumes: `IdleDetectionService`（Task 3 全部簽章）、`SettingsService`、`MonitorStatus`。
- Produces（Task 5 XAML 依賴）: `MainViewModel : ObservableObject, IDisposable`，新增屬性 `string FirstTriggerText`、`string NextMoveText`、`int TriggerCount`、`public IdleDetectionService IdleService { get; }`；建構子 `MainViewModel(SettingsService settingsService, Func<AppSettings, IdleDetectionService>? idleServiceFactory = null)`；`StartCommand` CanExecute=（Status==Paused）、`PauseCommand` CanExecute=（Status!=Paused）；`Settings.AutoStartMonitoring==true` 時建構後自動開始監控。

- [ ] **Step 1: 覆寫測試檔（先寫測試）**

`tests/MousePilot.Tests/MainViewModelTests.cs` 整檔改為：

```csharp
using MousePilot.Models;
using MousePilot.Services;
using MousePilot.ViewModels;

namespace MousePilot.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private sealed class TestClock
    {
        public uint Now;
        public uint LastInput;
    }

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "MousePilotTests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private MainViewModel CreateVm(bool autoStart, TestClock clock)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath,
            $"{{\"autoStartMonitoring\": {(autoStart ? "true" : "false")}, \"idleStartSeconds\": 5}}");
        return new MainViewModel(new SettingsService(SettingsPath),
            s => new IdleDetectionService(s, () => clock.Now, () => clock.LastInput, () => (7, 8)));
    }

    [Fact]
    public void 關閉自動開始時初始為已暫停()
    {
        var vm = CreateVm(autoStart: false, new TestClock());
        Assert.Equal(MonitorStatus.Paused, vm.Status);
        Assert.Equal("已暫停", vm.StatusText);
        Assert.True(vm.StartCommand.CanExecute(null));
        Assert.False(vm.PauseCommand.CanExecute(null));
    }

    [Fact]
    public void 開啟自動開始時建構後即在監控()
    {
        var clock = new TestClock { Now = 10_000, LastInput = 10_000 };
        var vm = CreateVm(autoStart: true, clock);
        Assert.Equal(MonitorStatus.UserActive, vm.Status);
        Assert.False(vm.StartCommand.CanExecute(null));
        Assert.True(vm.PauseCommand.CanExecute(null));
    }

    [Fact]
    public void 達門檻時觸發佔位事件並更新顯示()
    {
        var clock = new TestClock();
        var vm = CreateVm(autoStart: true, clock); // idleStart=5 秒
        clock.Now = 5_000;
        vm.IdleService.PollNow();

        Assert.Equal(1, vm.TriggerCount);
        Assert.Equal(MonitorStatus.WaitingToStart, vm.Status);
        Assert.Equal("等待啟動", vm.StatusText);
        Assert.Equal("X=7, Y=8", vm.MousePosition);
        Assert.Equal("30 秒", vm.NextMoveText);
        Assert.Equal("—", vm.FirstTriggerText);
    }

    [Fact]
    public void 監控中顯示第一次觸發倒數()
    {
        var clock = new TestClock();
        var vm = CreateVm(autoStart: true, clock);
        clock.Now = 3_000;
        vm.IdleService.PollNow();

        Assert.Equal(MonitorStatus.Monitoring, vm.Status);
        Assert.Equal(3.0, vm.IdleSeconds);
        Assert.Equal("2 秒", vm.FirstTriggerText);
        Assert.Equal("—", vm.NextMoveText);
    }

    [Fact]
    public void 暫停與啟動命令切換狀態()
    {
        var vm = CreateVm(autoStart: true, new TestClock());
        vm.PauseCommand.Execute(null);
        Assert.Equal(MonitorStatus.Paused, vm.Status);
        Assert.True(vm.StartCommand.CanExecute(null));

        vm.StartCommand.Execute(null);
        Assert.NotEqual(MonitorStatus.Paused, vm.Status);
        Assert.True(vm.PauseCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(MonitorStatus.Paused, "已暫停")]
    [InlineData(MonitorStatus.Monitoring, "監控中")]
    [InlineData(MonitorStatus.UserActive, "使用者活動中")]
    [InlineData(MonitorStatus.WaitingToStart, "等待啟動")]
    [InlineData(MonitorStatus.AutoMoving, "自動移動中")]
    public void StatusText由Status派生(MonitorStatus status, string expected)
    {
        var vm = CreateVm(autoStart: false, new TestClock());
        vm.Status = status;
        Assert.Equal(expected, vm.StatusText);
    }

    [Fact]
    public void 設定損毀時顯示非侵入式提示()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ broken");

        var vm = new MainViewModel(new SettingsService(SettingsPath),
            s => new IdleDetectionService(s, () => 0u, () => 0u, () => (0, 0)));

        Assert.Contains("預設值", vm.Notice);
        Assert.Equal(120, vm.Settings.IdleStartSeconds);
    }

    [Fact]
    public void SaveSettings會保存並夾制範圍()
    {
        var vm = CreateVm(autoStart: false, new TestClock());
        vm.Settings.IdleStartSeconds = 999999;

        vm.SaveSettings();

        var reloaded = new SettingsService(SettingsPath).Load().Settings;
        Assert.Equal(86400, reloaded.IdleStartSeconds);
    }

    [Fact]
    public void SaveSettings路徑不可寫時不擲例外並顯示提示()
    {
        Directory.CreateDirectory(_dir);
        var blockedDir = Path.Combine(_dir, "blocked");
        File.WriteAllText(blockedDir, "occupy");
        var vm = new MainViewModel(new SettingsService(Path.Combine(blockedDir, "settings.json")),
            s => new IdleDetectionService(s, () => 0u, () => 0u, () => (0, 0)));

        var ex = Record.Exception(() => vm.SaveSettings());

        Assert.Null(ex);
        Assert.Contains("保存失敗", vm.Notice);
    }
}
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 編譯失敗（`MainViewModel` 無 factory 建構子、無 `IdleService`/`TriggerCount` 等成員）。

- [ ] **Step 3: 覆寫 ViewModels/MainViewModel.cs**

```csharp
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MousePilot.Models;
using MousePilot.Services;

namespace MousePilot.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settingsService;

    public AppSettings Settings { get; }

    public IdleDetectionService IdleService { get; }

    [ObservableProperty]
    private MonitorStatus _status = MonitorStatus.Paused;

    [ObservableProperty]
    private string _statusText = "已暫停";

    [ObservableProperty]
    private double _idleSeconds;

    [ObservableProperty]
    private string _mousePosition = "—";

    [ObservableProperty]
    private string _cursorStatusText = "Windows 預設";

    [ObservableProperty]
    private string _notice = "";

    [ObservableProperty]
    private string _firstTriggerText = "—";

    [ObservableProperty]
    private string _nextMoveText = "—";

    [ObservableProperty]
    private int _triggerCount;

    public MainViewModel(
        SettingsService settingsService,
        Func<AppSettings, IdleDetectionService>? idleServiceFactory = null)
    {
        _settingsService = settingsService;
        var result = settingsService.Load();
        Settings = result.Settings;
        if (result.WasCorrupt)
        {
            Notice = result.BackupPath is null
                ? "設定檔損毀，已載入預設值。"
                : $"設定檔損毀，已載入預設值（原檔備份：{result.BackupPath}）。";
        }

        IdleService = (idleServiceFactory ?? (s => new IdleDetectionService(s)))(Settings);
        IdleService.Ticked += OnTicked;
        IdleService.MoveRequested += OnMoveRequested;

        if (Settings.AutoStartMonitoring)
        {
            StartMonitoring();
        }
    }

    private void OnTicked(IdleTickResult result, (int X, int Y) cursor)
    {
        Status = result.State;
        IdleSeconds = result.IdleSeconds;
        MousePosition = $"X={cursor.X}, Y={cursor.Y}";
        FirstTriggerText = result.SecondsUntilFirstTrigger is { } f ? $"{f:F0} 秒" : "—";
        NextMoveText = result.SecondsUntilNextMove is { } n ? $"{n:F0} 秒" : "—";
    }

    // Phase 3 接上 MouseMovementService 執行實際移動；目前僅累計佔位事件
    private void OnMoveRequested() => TriggerCount++;

    partial void OnStatusChanged(MonitorStatus value)
    {
        StatusText = value switch
        {
            MonitorStatus.Paused => "已暫停",
            MonitorStatus.Monitoring => "監控中",
            MonitorStatus.UserActive => "使用者活動中",
            MonitorStatus.WaitingToStart => "等待啟動",
            MonitorStatus.AutoMoving => "自動移動中",
            _ => value.ToString(),
        };
        StartCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
    }

    public void SaveSettings()
    {
        try
        {
            _settingsService.Save(Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // 保存失敗不可讓程式 crash（規格 §21）；Phase 11 接上 LogService 後記錄
            Notice = $"設定保存失敗：{ex.Message}";
        }
    }

    private void StartMonitoring()
    {
        Settings.Clamp();
        IdleService.Start();
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start() => StartMonitoring();

    private bool CanStart() => Status == MonitorStatus.Paused;

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause() => IdleService.Pause();

    private bool CanPause() => Status != MonitorStatus.Paused;

    public void Dispose() => IdleService.Dispose();
}
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 全綠（Task 3 後 52 − 舊 VM 測試 10（5 fact + Task 1 的 5 theory 案例）+ 新 VM 測試 13（8 fact + 5 theory 案例）= 55）。若總數與此推算不同，以「無 FAIL、無 SKIP」為準並在報告說明計數。

- [ ] **Step 5: Commit**

```text
feat: MainViewModel 整合閒置偵測 - 啟停命令與狀態顯示生效

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

`git add ViewModels/MainViewModel.cs tests/MousePilot.Tests/MainViewModelTests.cs`，`-F` 提交後驗證訊息。

---

### Task 5: XAML 狀態區更新與 App 接線

**Files:**
- Modify: `Views/MainWindow.xaml`（狀態卡）
- Modify: `App.xaml.cs`（Dispose）

**Interfaces:**
- Consumes: `MainViewModel` 的 `Status`/`FirstTriggerText`/`NextMoveText`/`TriggerCount`/`IdleSeconds`/`MousePosition`（Task 4）。

- [ ] **Step 1: 更新 Views/MainWindow.xaml 狀態卡**

把狀態卡（`<TextBlock Text="狀態" .../>` 所在的 `<Border>`）內容替換為：

```xml
                <StackPanel>
                    <TextBlock Text="狀態" Style="{StaticResource CardTitle}"/>
                    <StackPanel Orientation="Horizontal" Margin="0,0,0,10">
                        <Ellipse Width="10" Height="10" VerticalAlignment="Center">
                            <Ellipse.Style>
                                <Style TargetType="Ellipse">
                                    <Setter Property="Fill" Value="{StaticResource StatusPausedBrush}"/>
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding Status}" Value="Monitoring">
                                            <Setter Property="Fill" Value="{StaticResource StatusRunningBrush}"/>
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding Status}" Value="UserActive">
                                            <Setter Property="Fill" Value="{StaticResource StatusRunningBrush}"/>
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding Status}" Value="WaitingToStart">
                                            <Setter Property="Fill" Value="{StaticResource StatusWaitingBrush}"/>
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding Status}" Value="AutoMoving">
                                            <Setter Property="Fill" Value="{StaticResource StatusWaitingBrush}"/>
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </Ellipse.Style>
                        </Ellipse>
                        <TextBlock Text="{Binding StatusText}" Margin="8,0,0,0"
                                   FontSize="16" FontWeight="SemiBold"/>
                    </StackPanel>
                    <UniformGrid Columns="2" Rows="3">
                        <TextBlock>
                            <Run Text="閒置秒數："/><Run Text="{Binding IdleSeconds, Mode=OneWay, StringFormat={}{0:F0} 秒}"/>
                        </TextBlock>
                        <TextBlock>
                            <Run Text="滑鼠座標："/><Run Text="{Binding MousePosition, Mode=OneWay}"/>
                        </TextBlock>
                        <TextBlock>
                            <Run Text="距離第一次觸發："/><Run Text="{Binding FirstTriggerText, Mode=OneWay}"/>
                        </TextBlock>
                        <TextBlock>
                            <Run Text="距離下一次移動："/><Run Text="{Binding NextMoveText, Mode=OneWay}"/>
                        </TextBlock>
                        <TextBlock>
                            <Run Text="游標："/><Run Text="{Binding CursorStatusText, Mode=OneWay}"/>
                        </TextBlock>
                        <TextBlock>
                            <Run Text="觸發次數："/><Run Text="{Binding TriggerCount, Mode=OneWay}"/>
                        </TextBlock>
                    </UniformGrid>
                    <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
                        <Button Content="啟動 MousePilot" Command="{Binding StartCommand}"
                                Padding="16,8" Margin="0,0,8,0"/>
                        <Button Content="暫停" Command="{Binding PauseCommand}" Padding="16,8"/>
                    </StackPanel>
                </StackPanel>
```

另外把系統設定卡中的 checkbox 文案 `啟動程式後自動開始監控（Phase 2 生效）` 改為 `啟動程式後自動開始監控`。

- [ ] **Step 2: 更新 App.xaml.cs 的 OnExit**

```csharp
    protected override void OnExit(ExitEventArgs e)
    {
        _mainViewModel?.SaveSettings();
        _mainViewModel?.Dispose();
        base.OnExit(e);
    }
```

- [ ] **Step 3: Build + 測試 + 啟動冒煙**

Run: `dotnet build -c Release`（0 error）、`dotnet test tests/MousePilot.Tests`（全綠）。
啟動冒煙（無螢幕替代）：背景啟動 app、等 4 秒確認程序存活、Stop-Process 收掉，如實記錄。

- [ ] **Step 4: Commit**

```text
feat: 狀態卡完成 - 顏色點派生/倒數/觸發次數與服務釋放

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

`git add Views/MainWindow.xaml App.xaml.cs`，`-F` 提交後驗證訊息。

---

### Task 6: Phase 收尾

**Files:**
- Modify: `CHANGELOG.md`
- Modify: `docs/superpowers/plans/2026-08-20-mousepilot-master-plan.md`（Phase 2 → ✅ 完成）

- [ ] **Step 1: CHANGELOG [Unreleased] 的「### 新增」補上**

```markdown
- 閒置偵測（Phase 2）：GetLastInputInfo + 500ms 輪詢狀態機、五種狀態與顏色點、閒置/倒數/座標/觸發次數即時顯示、啟動/暫停與「啟動後自動開始監控」生效。
- 模擬輸入抑制窗 API（Suppress）：依 Spike A 結論，供 Phase 3 滑鼠移動時避免誤判為使用者操作。
```

- [ ] **Step 2: Master Plan 進度表 Phase 2 列改為「✅ 完成」，細部計畫文件欄填 `2026-08-20-phase2-idle-detection.md`**

- [ ] **Step 3: 最終驗證**

```powershell
dotnet build -c Release
dotnet test tests/MousePilot.Tests
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

Expected: 全部成功；EXE 位於 `bin\Release\net8.0-windows\win-x64\publish\MousePilot.exe`。

- [ ] **Step 4: Commit**

```text
docs: 更新 CHANGELOG 與進度總表 - Phase 2 完成

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

`git add CHANGELOG.md docs/superpowers/plans/2026-08-20-mousepilot-master-plan.md`，`-F` 提交後驗證訊息。

---

## Phase 2 完成定義

- [ ] `dotnet build -c Release` 0 error、`dotnet test` 全綠、publish 成功。
- [ ] 狀態機單元測試涵蓋：五狀態轉移、門檻/間隔觸發、真實輸入立即取消、抑制窗內外行為、tick 繞回。
- [ ] **使用者實機手動驗證（規格 §34 Idle 1~5，建議把「開始閒置」暫調為 10 秒測試）：**
  1. 持續打字 → 不觸發（狀態停在 使用者活動中/監控中）。
  2. 持續移動滑鼠 → 不觸發。
  3. 雙手放開 10 秒 → 狀態變 等待啟動、觸發次數 +1（本階段只加計數，滑鼠不會動）。
  4. 倒數期間動一下滑鼠 → 立即回 使用者活動中、倒數重來。
  5. 持續放開 → 觸發次數每隔「後續移動間隔」+1。
  - 另驗：按「暫停」→ 灰點/已暫停；按「啟動」→ 恢復；重開程式（AutoStartMonitoring 開啟時）→ 自動開始監控。
