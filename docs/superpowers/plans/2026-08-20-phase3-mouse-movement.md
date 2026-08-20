# MousePilot Phase 3：Mouse Movement 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 實際滑鼠微移全功能：左右/上下/隨機三模式、1~100px、回原位置、螢幕邊界反向、多螢幕負座標、使用者輸入立即取消（含返回等待期）、Dashboard「立即執行一次」。

**Architecture:** `MovementPlanner`（純函式：位移計算 + 邊界反向）→ `MouseMovementService`（單次移動流程：單一抑制窗涵蓋移動+返回、返回前雙重輸入檢查、全 provider 可注入）→ VM 接 `MoveRequested` 事件與 `MoveOnceCommand`。座標正規化放 `ScreenBounds`（純、可測），SendInput/GetSystemMetrics/SetCursorPos 進 `NativeMethods`。移動用 **SendInput 絕對座標**（會產生真實輸入事件→防閒置生效；SetCursorPos 不產生輸入事件、只用於 ±1px 精準校正）。

**Tech Stack:** 既有，無新相依。

**Spec:** `docs/spec/mousepilot-spec.md`（§4/§5/§6/§14/§19/§22/§23/§24、§34 案例 5~12）；`docs/superpowers/research/2026-08-20-spike-findings.md`；Master Plan Phase 3 章節（含移交約束）。

## Global Constraints

- **Phase 2 移交約束（全部硬性）：**
  1. 一次 `Suppress` 涵蓋整個「移動＋返回」動作（不分兩次 Suppress）。
  2. `MoveRequested`/`Ticked` 訂閱端不得拋例外——VM 的移動執行以 try/catch 包住、失敗寫 `Notice`。
  3. `AutoMoving` 進入/離開：狀態機新增 `BeginMove()`/`EndMove()`；移動期間 Tick 不再發 `MoveRequested`；真實輸入採納時一併清除移動中旗標。
  4. 返回移動執行前必須再檢查輸入：`GetLastInputInfo` 與送出後基準比對（抓鍵盤）＋目前游標是否仍在 target（抓滑鼠），任一不符即放棄返回。
  5. `NativeMethods.GetCursorPosition` 失敗改回傳 `null`（不再回 `(0,0)`）；讀不到游標時整次移動保守放棄。
- 返回等待固定 `300ms`（規格 §5 的 100~500ms 取合理預設，常數 `ReturnDelayMs`）；取消用 `CancellationToken` 貫穿（規格 §24）。
- 座標不得假設非負；一律以 Virtual Screen Bounds 計算（規格 §22）；越界軸自動反向，反向仍越界則夾在邊界（規格 §23）；隨機模式目標也必須驗證。
- 左右模式為往返（+p、−p 交替），上下同理；隨機 8 方向，距離由移動像素控制（規格 §4）。
- Win32 失敗一律保守：SendInput 失敗→中止本次；GetSystemMetrics 失敗→fallback (0,0,1920,1080)。
- 無新 NuGet 相依；PInvoke 只在 `Native/NativeMethods.cs`。
- TDD：先測試（RED）後實作（GREEN），輸出留存報告；目前基準 56 綠。
- Commit 一律 `git commit -F <暫存檔>`（禁 here-string），繁中訊息+前綴+`Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`，commit 後 `git log -1 --format=%B` 驗證。

---

### Task 1: ScreenBounds + NativeMethods 擴充 + 游標 nullable 化

**Files:**
- Create: `Models/ScreenBounds.cs`
- Modify: `Native/NativeMethods.cs`（GetCursorPosition 改 nullable；新增 SendInput/GetSystemMetrics/SetCursorPos）
- Modify: `Services/IdleDetectionService.cs`（cursorProvider 與 Ticked 事件改 nullable）
- Modify: `ViewModels/MainViewModel.cs`（OnTicked 顯示 null → "—"）
- Test: `tests/MousePilot.Tests/ScreenBoundsTests.cs`（新）；`tests/MousePilot.Tests/MainViewModelTests.cs`（加一測試）

**Interfaces:**
- Consumes: 既有 IdleDetectionService/MainViewModel。
- Produces（Task 4 依賴，逐字）:
  - `readonly record struct MousePilot.Models.ScreenBounds(int Left, int Top, int Width, int Height)`：`int Right`、`int Bottom`、`bool Contains(int x, int y)`、`(int Nx, int Ny) ToAbsolute(int x, int y)`
  - `NativeMethods`：`internal static (int X, int Y)? GetCursorPosition()`、`internal static ScreenBounds GetVirtualScreenBounds()`、`internal static bool SendMouseMoveAbsolute(int x, int y, ScreenBounds bounds)`、`internal static bool SetCursorPosition(int x, int y)`
  - `IdleDetectionService`：cursorProvider 型別改 `Func<(int X, int Y)?>?`；`event Action<IdleTickResult, (int X, int Y)?>? Ticked`

- [ ] **Step 1: 寫失敗測試**

`tests/MousePilot.Tests/ScreenBoundsTests.cs`：

```csharp
using MousePilot.Models;

namespace MousePilot.Tests;

public class ScreenBoundsTests
{
    [Fact]
    public void Right與Bottom為含端點邊界()
    {
        var b = new ScreenBounds(0, 0, 1920, 1080);
        Assert.Equal(1919, b.Right);
        Assert.Equal(1079, b.Bottom);
    }

    [Fact]
    public void Contains判斷內部與邊界點()
    {
        var b = new ScreenBounds(0, 0, 1920, 1080);
        Assert.True(b.Contains(0, 0));
        Assert.True(b.Contains(1919, 1079));
        Assert.False(b.Contains(1920, 0));
        Assert.False(b.Contains(0, -1));
    }

    [Fact]
    public void 負座標螢幕Contains正確()
    {
        var b = new ScreenBounds(-1920, -500, 3840, 1580); // 左側+上方延伸的虛擬桌面
        Assert.True(b.Contains(-1920, -500));
        Assert.True(b.Contains(1919, 1079));
        Assert.False(b.Contains(-1921, 0));
    }

    [Fact]
    public void ToAbsolute正規化為0到65535()
    {
        var b = new ScreenBounds(0, 0, 1920, 1080);
        Assert.Equal((0, 0), b.ToAbsolute(0, 0));
        Assert.Equal((65535, 65535), b.ToAbsolute(1919, 1079));
        Assert.Equal(32785, b.ToAbsolute(960, 0).Nx); // round(960*65535/1919) = round(32784.575)
    }

    [Fact]
    public void 負座標原點ToAbsolute為0()
    {
        var b = new ScreenBounds(-1920, 0, 3840, 1080);
        Assert.Equal(0, b.ToAbsolute(-1920, 0).Nx);
        Assert.Equal(65535, b.ToAbsolute(1919, 0).Nx);
    }
}
```

`tests/MousePilot.Tests/MainViewModelTests.cs` 新增（放在 `監控中顯示第一次觸發倒數` 之後）：

```csharp
    [Fact]
    public void 游標讀取失敗時顯示破折號()
    {
        var clock = new TestClock();
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{\"autoStartMonitoring\": true, \"idleStartSeconds\": 5}");
        var vm = new MainViewModel(new SettingsService(SettingsPath),
            s => new IdleDetectionService(s, () => clock.Now, () => clock.LastInput, () => null));
        vm.IdleService.PollNow();
        Assert.Equal("—", vm.MousePosition);
    }
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 編譯失敗（`ScreenBounds` 不存在；cursorProvider 尚非 nullable，`() => null` 無法編譯）。

- [ ] **Step 3: 實作**

`Models/ScreenBounds.cs`：

```csharp
namespace MousePilot.Models;

/// <summary>虛擬螢幕範圍（可含負座標，規格 §22）。端點含邊界。</summary>
public readonly record struct ScreenBounds(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width - 1;

    public int Bottom => Top + Height - 1;

    public bool Contains(int x, int y) => x >= Left && x <= Right && y >= Top && y <= Bottom;

    /// <summary>把虛擬螢幕座標正規化為 SendInput 絕對座標（0~65535，搭配 MOUSEEVENTF_VIRTUALDESK）。</summary>
    public (int Nx, int Ny) ToAbsolute(int x, int y) => (
        (int)Math.Round((x - Left) * 65535.0 / Math.Max(1, Width - 1)),
        (int)Math.Round((y - Top) * 65535.0 / Math.Max(1, Height - 1)));
}
```

`Native/NativeMethods.cs` 整檔覆寫：

```csharp
using System.Runtime.InteropServices;
using MousePilot.Models;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    /// <summary>最後一次輸入的 tick（毫秒）。失敗時回傳目前 tick——視為「剛有輸入」的保守值，確保不誤觸發移動（規格 §40-1）。</summary>
    internal static uint GetLastInputTick()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info) ? info.dwTime : (uint)Environment.TickCount;
    }

    /// <summary>目前游標座標；失敗回傳 null（(0,0) 在負座標桌面是合法座標，不可作 fallback——Phase 2 移交約束 5）。</summary>
    internal static (int X, int Y)? GetCursorPosition()
        => GetCursorPos(out var p) ? (p.X, p.Y) : null;

    /// <summary>虛擬螢幕範圍；GetSystemMetrics 失敗（回 0）時用保守預設 1920x1080。</summary>
    internal static ScreenBounds GetVirtualScreenBounds()
    {
        var w = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var h = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (w <= 0 || h <= 0)
        {
            return new ScreenBounds(0, 0, 1920, 1080);
        }

        return new ScreenBounds(GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN), w, h);
    }

    /// <summary>以絕對座標送出一次滑鼠移動。會產生真實輸入事件（防閒置的核心；SetCursorPos 不會）。</summary>
    internal static bool SendMouseMoveAbsolute(int x, int y, ScreenBounds bounds)
    {
        var (nx, ny) = bounds.ToAbsolute(x, y);
        var inputs = new[]
        {
            new INPUT
            {
                type = INPUT_MOUSE,
                mi = new MOUSEINPUT { dx = nx, dy = ny, dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK },
            },
        };
        return SendInput(1, inputs, Marshal.SizeOf<INPUT>()) == 1;
    }

    /// <summary>精準設定游標位置（不產生輸入事件，僅供 ±1px 校正）。</summary>
    internal static bool SetCursorPosition(int x, int y) => SetCursorPos(x, y);
}
```

`Services/IdleDetectionService.cs` 修改三處：

```csharp
    private readonly Func<(int X, int Y)?> _cursorProvider;
```

```csharp
    public event Action<IdleTickResult, (int X, int Y)?>? Ticked;
```

```csharp
    public IdleDetectionService(
        AppSettings settings,
        Func<uint>? tickProvider = null,
        Func<uint>? lastInputProvider = null,
        Func<(int X, int Y)?>? cursorProvider = null)
```

（建構子內 `_cursorProvider = cursorProvider ?? NativeMethods.GetCursorPosition;` 不變。）

`ViewModels/MainViewModel.cs` 的 `OnTicked` 簽章與座標顯示改為：

```csharp
    private void OnTicked(IdleTickResult result, (int X, int Y)? cursor)
    {
        Status = result.State;
        IdleSeconds = result.IdleSeconds;
        MousePosition = cursor is { } c ? $"X={c.X}, Y={c.Y}" : "—";
        FirstTriggerText = result.SecondsUntilFirstTrigger is { } f ? $"{f:F0} 秒" : "—";
        NextMoveText = result.SecondsUntilNextMove is { } n ? $"{n:F0} 秒" : "—";
    }
```

（既有測試的 `() => (7, 8)` lambda 對 nullable 目標型別可直接隱式轉換，不需改動。）

- [ ] **Step 4: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 全綠（56 + 6 = 62）。

- [ ] **Step 5: Commit**

```text
feat: ScreenBounds 與 SendInput PInvoke - 游標讀取 nullable 化

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

`git add Models/ScreenBounds.cs Native/NativeMethods.cs Services/IdleDetectionService.cs ViewModels/MainViewModel.cs tests/MousePilot.Tests/ScreenBoundsTests.cs tests/MousePilot.Tests/MainViewModelTests.cs`，`-F` 提交後驗證。

---

### Task 2: IdleStateMachine 移動中狀態（TDD）

**Files:**
- Modify: `Services/IdleStateMachine.cs`
- Test: `tests/MousePilot.Tests/IdleStateMachineTests.cs`（新增測試）

**Interfaces:**
- Produces（Task 4/5 依賴）: `void BeginMove()`（僅 `_running && _autoCycleActive` 時生效：State=AutoMoving，Tick 期間不發 MoveRequested）、`void EndMove()`（清旗標；仍在自動週期則 State=WaitingToStart）。真實輸入採納時同時清除移動中旗標。

- [ ] **Step 1: 寫失敗測試**

在 `IdleStateMachineTests.cs` 新增：

```csharp
    [Fact]
    public void BeginMove後狀態為自動移動中且不再觸發()
    {
        var m = Started();
        m.Tick(120_000, 0, Threshold, Interval);       // 觸發、進入週期
        m.BeginMove();
        Assert.Equal(MonitorStatus.AutoMoving, m.State);
        var r = m.Tick(150_100, 0, Threshold, Interval); // 已超過間隔，但移動中不得再觸發
        Assert.Equal(MonitorStatus.AutoMoving, r.State);
        Assert.False(r.MoveRequested);
    }

    [Fact]
    public void EndMove後回到等待啟動()
    {
        var m = Started();
        m.Tick(120_000, 0, Threshold, Interval);
        m.BeginMove();
        m.EndMove();
        Assert.Equal(MonitorStatus.WaitingToStart, m.State);
        var r = m.Tick(150_000, 0, Threshold, Interval); // 間隔到 → 恢復觸發
        Assert.True(r.MoveRequested);
    }

    [Fact]
    public void 移動中偵測到真實輸入立即取消移動旗標與週期()
    {
        var m = Started();
        m.Tick(120_000, 0, Threshold, Interval);
        m.BeginMove();
        var r = m.Tick(121_000, 120_800, Threshold, Interval); // 真實使用者輸入
        Assert.Equal(MonitorStatus.UserActive, r.State);
        m.EndMove();                                            // caller finally 呼叫
        Assert.Equal(MonitorStatus.UserActive, m.State);        // 不得誤回等待啟動
    }

    [Fact]
    public void 未在自動週期時BeginMove無效()
    {
        var m = Started();
        m.Tick(10_000, 0, Threshold, Interval);        // Monitoring，未觸發
        m.BeginMove();
        Assert.Equal(MonitorStatus.Monitoring, m.State);
    }

    [Fact]
    public void Pause清除移動中旗標()
    {
        var m = Started();
        m.Tick(120_000, 0, Threshold, Interval);
        m.BeginMove();
        m.Pause();
        Assert.Equal(MonitorStatus.Paused, m.State);
        m.Start(120_000, 120_000);
        var r = m.Tick(120_500, 120_000, Threshold, Interval);
        Assert.Equal(MonitorStatus.UserActive, r.State); // 不殘留 AutoMoving
    }
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 編譯失敗（`BeginMove`/`EndMove` 不存在）。

- [ ] **Step 3: 實作（`Services/IdleStateMachine.cs` 修改）**

新增欄位與方法：

```csharp
    private bool _moveInProgress;
```

```csharp
    /// <summary>實際移動動作開始（Phase 3）。僅在自動週期內生效；移動期間 State=AutoMoving 且 Tick 不發 MoveRequested。</summary>
    public void BeginMove()
    {
        if (_running && _autoCycleActive)
        {
            _moveInProgress = true;
            State = MonitorStatus.AutoMoving;
        }
    }

    /// <summary>移動動作結束（caller 必須在 finally 呼叫）。仍在自動週期才回到等待啟動。</summary>
    public void EndMove()
    {
        _moveInProgress = false;
        if (_running && _autoCycleActive)
        {
            State = MonitorStatus.WaitingToStart;
        }
    }
```

`Pause()` 內加一行 `_moveInProgress = false;`。

`Tick` 兩處修改：
1. 真實輸入採納區塊（`_autoCycleActive = false;` 之後）加 `_moveInProgress = false;`。
2. `_autoCycleActive` 為 true 的分支開頭（`var sinceMove = ...` 之前）加：

```csharp
        if (_moveInProgress)
        {
            State = MonitorStatus.AutoMoving;
            return new IdleTickResult(State, idleSeconds, null, null, false);
        }
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 全綠（62 + 5 = 67）。

- [ ] **Step 5: Commit**

```text
feat: IdleStateMachine 移動中狀態 - BeginMove/EndMove 與觸發抑制

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

### Task 3: MovementPlanner（TDD 純函式）

**Files:**
- Create: `Services/MovementPlanner.cs`
- Test: `tests/MousePilot.Tests/MovementPlannerTests.cs`

**Interfaces:**
- Produces（Task 4 依賴，逐字）:
  - `static class MovementPlanner`：`static (int Dx, int Dy) NextOffset(MovementMode mode, int pixels, bool togglePositive, int randomIndex)`、`static (int X, int Y) ApplyWithinBounds((int X, int Y) pos, (int Dx, int Dy) offset, ScreenBounds bounds)`
  - 隨機方向表順序（randomIndex 0~7）：上、下、左、右、左上、右上、左下、右下。

- [ ] **Step 1: 寫失敗測試**

`tests/MousePilot.Tests/MovementPlannerTests.cs`：

```csharp
using MousePilot.Models;
using MousePilot.Services;

namespace MousePilot.Tests;

public class MovementPlannerTests
{
    private static readonly ScreenBounds Screen = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData(true, 3, 0)]
    [InlineData(false, -3, 0)]
    public void 左右模式往返(bool toggle, int dx, int dy)
    {
        Assert.Equal((dx, dy), MovementPlanner.NextOffset(MovementMode.Horizontal, 3, toggle, 0));
    }

    [Theory]
    [InlineData(true, 0, 3)]
    [InlineData(false, 0, -3)]
    public void 上下模式往返(bool toggle, int dx, int dy)
    {
        Assert.Equal((dx, dy), MovementPlanner.NextOffset(MovementMode.Vertical, 3, toggle, 0));
    }

    [Theory]
    [InlineData(0, 0, -5)]   // 上
    [InlineData(1, 0, 5)]    // 下
    [InlineData(2, -5, 0)]   // 左
    [InlineData(3, 5, 0)]    // 右
    [InlineData(4, -5, -5)]  // 左上
    [InlineData(5, 5, -5)]   // 右上
    [InlineData(6, -5, 5)]   // 左下
    [InlineData(7, 5, 5)]    // 右下
    public void 隨機模式八方向距離由像素控制(int index, int dx, int dy)
    {
        Assert.Equal((dx, dy), MovementPlanner.NextOffset(MovementMode.Random, 5, true, index));
    }

    [Fact]
    public void 範圍內位移直接套用()
    {
        Assert.Equal((503, 300), MovementPlanner.ApplyWithinBounds((500, 300), (3, 0), Screen));
    }

    [Fact]
    public void 右緣越界自動反向()
    {
        Assert.Equal((1916, 300), MovementPlanner.ApplyWithinBounds((1919, 300), (3, 0), Screen));
    }

    [Fact]
    public void 負座標螢幕左緣反向()
    {
        var multi = new ScreenBounds(-1920, 0, 3840, 1080);
        Assert.Equal((-1917, 300), MovementPlanner.ApplyWithinBounds((-1920, 300), (-3, 0), multi));
    }

    [Fact]
    public void 角落雙軸同時反向()
    {
        Assert.Equal((1916, 1076), MovementPlanner.ApplyWithinBounds((1919, 1079), (3, 3), Screen));
    }

    [Fact]
    public void 反向仍越界時夾在邊界()
    {
        var tiny = new ScreenBounds(0, 0, 50, 50);
        Assert.Equal((0, 25), MovementPlanner.ApplyWithinBounds((10, 25), (100, 0), tiny)); // 10-100=-90 → 夾 0
    }
}
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 編譯失敗（`MovementPlanner` 不存在）。

- [ ] **Step 3: 實作 Services/MovementPlanner.cs**

```csharp
using MousePilot.Models;

namespace MousePilot.Services;

/// <summary>移動目標計算（純函式，無 Win32、無隨機源——randomIndex 由呼叫端提供）。</summary>
public static class MovementPlanner
{
    // 順序：上、下、左、右、左上、右上、左下、右下（規格 §4 隨機模式）
    private static readonly (int Dx, int Dy)[] RandomDirections =
    {
        (0, -1), (0, 1), (-1, 0), (1, 0), (-1, -1), (1, -1), (-1, 1), (1, 1),
    };

    public static (int Dx, int Dy) NextOffset(MovementMode mode, int pixels, bool togglePositive, int randomIndex)
        => mode switch
        {
            MovementMode.Horizontal => (togglePositive ? pixels : -pixels, 0),
            MovementMode.Vertical => (0, togglePositive ? pixels : -pixels),
            _ => (RandomDirections[randomIndex & 7].Dx * pixels, RandomDirections[randomIndex & 7].Dy * pixels),
        };

    /// <summary>套用位移並確保結果在虛擬螢幕內：越界軸自動反向（規格 §23），反向仍越界則夾在邊界。</summary>
    public static (int X, int Y) ApplyWithinBounds((int X, int Y) pos, (int Dx, int Dy) offset, ScreenBounds bounds)
        => (ReflectAxis(pos.X, offset.Dx, bounds.Left, bounds.Right),
            ReflectAxis(pos.Y, offset.Dy, bounds.Top, bounds.Bottom));

    private static int ReflectAxis(int value, int delta, int min, int max)
    {
        var target = value + delta;
        if (target >= min && target <= max)
        {
            return target;
        }

        return Math.Clamp(value - delta, min, max);
    }
}
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 全綠（67 + 17 = 84）。

- [ ] **Step 5: Commit**

```text
feat: MovementPlanner - 三模式位移與螢幕邊界反向

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

### Task 4: MouseMovementService（TDD）

**Files:**
- Create: `Services/MouseMovementService.cs`
- Modify: `Services/IdleDetectionService.cs`（新增 `BeginMove()`/`EndMove()` 轉呼叫）
- Test: `tests/MousePilot.Tests/MouseMovementServiceTests.cs`

**Interfaces:**
- Consumes: `MovementPlanner`（Task 3）、`ScreenBounds`（Task 1）、`IdleDetectionService.Suppress`、`IdleStateMachine.BeginMove/EndMove`（Task 2，經 service 轉呼叫）。
- Produces（Task 5 依賴，逐字）:
  - `IdleDetectionService` 新增：`public void BeginMove() => _machine.BeginMove();`、`public void EndMove() => _machine.EndMove();`
  - `sealed class MouseMovementService`：常數 `public const int ReturnDelayMs = 300;`；建構子 `MouseMovementService(AppSettings settings, IdleDetectionService idleService, Func<(int X, int Y)?>? cursorProvider = null, Func<ScreenBounds>? boundsProvider = null, Func<int, int, bool>? sendMove = null, Func<int, int, bool>? correctPosition = null, Func<uint>? lastInputProvider = null, Func<TimeSpan, CancellationToken, Task>? delay = null, Func<int>? randomIndexProvider = null)`；`Task<bool> ExecuteMoveAsync(CancellationToken ct)`（true=完整執行，false=取消/放棄）。

- [ ] **Step 1: 寫失敗測試**

`tests/MousePilot.Tests/MouseMovementServiceTests.cs`：

```csharp
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
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 編譯失敗（`MouseMovementService` 不存在）。

- [ ] **Step 3: 實作**

`Services/IdleDetectionService.cs` 新增兩個轉呼叫（放在 `Suppress` 之後）：

```csharp
    /// <summary>實際移動開始/結束（Phase 3；轉呼叫狀態機）。</summary>
    public void BeginMove() => _machine.BeginMove();

    public void EndMove() => _machine.EndMove();
```

`Services/MouseMovementService.cs`：

```csharp
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
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 全綠（84 + 10 = 94）。

- [ ] **Step 5: Commit**

```text
feat: MouseMovementService - 單次移動流程與返回防線

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

### Task 5: VM 與 XAML 整合（TDD）

**Files:**
- Modify: `ViewModels/MainViewModel.cs`
- Modify: `Views/MainWindow.xaml`（狀態卡按鈕列加「立即執行一次」）
- Test: `tests/MousePilot.Tests/MainViewModelTests.cs`（新增測試）

**Interfaces:**
- Consumes: `MouseMovementService.ExecuteMoveAsync`、`IdleDetectionService.BeginMove/EndMove`（Task 4）。
- Produces: `MainViewModel` 建構子第三參數 `Func<AppSettings, IdleDetectionService, MouseMovementService>? movementServiceFactory = null`；`MoveOnceCommand`（IAsyncRelayCommand，隨時可按）；自動觸發流程：`MoveRequested` → TriggerCount++ → BeginMove → ExecuteMoveAsync → finally EndMove；`Ticked` 為 UserActive 時取消進行中移動；例外一律吞入 `Notice`（移交約束 2）。

- [ ] **Step 1: 寫失敗測試**

在 `MainViewModelTests.cs`：

1. `CreateVm` helper 改為（支援 movement fake，記錄送出座標）：

```csharp
    private sealed class MoveRecorder
    {
        public List<(int X, int Y)> Sent { get; } = new();
        public bool ThrowOnMove;
    }

    private MainViewModel CreateVm(bool autoStart, TestClock clock, MoveRecorder? recorder = null)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath,
            $"{{\"autoStartMonitoring\": {(autoStart ? "true" : "false")}, \"idleStartSeconds\": 5, \"returnToOriginalPosition\": false}}");
        return new MainViewModel(new SettingsService(SettingsPath),
            s => new IdleDetectionService(s, () => clock.Now, () => clock.LastInput, () => (7, 8)),
            (s, idle) => new MouseMovementService(
                s, idle,
                cursorProvider: () => (500, 300),
                boundsProvider: () => new ScreenBounds(0, 0, 1920, 1080),
                sendMove: (x, y) =>
                {
                    if (recorder?.ThrowOnMove == true) { throw new InvalidOperationException("boom"); }
                    recorder?.Sent.Add((x, y));
                    return true;
                },
                correctPosition: (_, _) => true,
                lastInputProvider: () => 0u,
                delay: (_, _) => Task.CompletedTask,
                randomIndexProvider: () => 3));
    }
```

（既有測試呼叫 `CreateVm(autoStart, clock)` 不受影響；`using MousePilot.Models;` 已在檔頭。）

2. 新增測試：

```csharp
    [Fact]
    public async Task 立即執行一次會送出移動()
    {
        var recorder = new MoveRecorder();
        var vm = CreateVm(autoStart: false, new TestClock(), recorder);
        await vm.MoveOnceCommand.ExecuteAsync(null);
        Assert.Single(recorder.Sent);
        Assert.Equal((503, 300), recorder.Sent[0]); // Horizontal 預設 3px
    }

    [Fact]
    public void 自動觸發時執行移動並累計次數()
    {
        var recorder = new MoveRecorder();
        var clock = new TestClock();
        var vm = CreateVm(autoStart: true, clock, recorder);
        clock.Now = 5_000;
        vm.IdleService.PollNow();          // 觸發 → MoveRequested → 移動（delay 為同步完成）
        Assert.Equal(1, vm.TriggerCount);
        Assert.Single(recorder.Sent);
    }

    [Fact]
    public async Task 移動例外不外拋僅顯示提示()
    {
        var recorder = new MoveRecorder { ThrowOnMove = true };
        var vm = CreateVm(autoStart: false, new TestClock(), recorder);
        var ex = await Record.ExceptionAsync(() => vm.MoveOnceCommand.ExecuteAsync(null));
        Assert.Null(ex);
        Assert.Contains("移動失敗", vm.Notice);
    }
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 編譯失敗（VM 無第三參數/`MoveOnceCommand`）。

- [ ] **Step 3: 實作（`ViewModels/MainViewModel.cs` 修改）**

1. 新增欄位與建構子第三參數：

```csharp
    private readonly MouseMovementService _movementService;
    private CancellationTokenSource? _moveCts;
    private bool _moving;
```

```csharp
    public MainViewModel(
        SettingsService settingsService,
        Func<AppSettings, IdleDetectionService>? idleServiceFactory = null,
        Func<AppSettings, IdleDetectionService, MouseMovementService>? movementServiceFactory = null)
```

建構子內、`IdleService.Ticked += OnTicked;` 之前加：

```csharp
        _movementService = (movementServiceFactory ?? ((s, i) => new MouseMovementService(s, i)))(Settings, IdleService);
```

2. `OnTicked` 內（`Status = result.State;` 之前）加真實輸入取消：

```csharp
        if (result.State == MonitorStatus.UserActive)
        {
            _moveCts?.Cancel(); // 真實使用者輸入→立即取消進行中的移動（規格 §24）
        }
```

3. `OnMoveRequested` 與新命令改為：

```csharp
    // 訂閱端不得拋例外（Phase 2 移交約束 2）：整個移動流程包在 guarded 方法內
    private async void OnMoveRequested() => await ExecuteMoveGuardedAsync(fromAutoCycle: true);

    /// <summary>立即執行一次（規格 §14）：不等 Idle Timer，依目前設定執行。</summary>
    [RelayCommand]
    private async Task MoveOnceAsync() => await ExecuteMoveGuardedAsync(fromAutoCycle: false);

    private async Task ExecuteMoveGuardedAsync(bool fromAutoCycle)
    {
        if (_moving)
        {
            return; // 防重入：自動觸發與手動執行不重疊
        }

        _moving = true;
        var cts = new CancellationTokenSource();
        _moveCts = cts;
        try
        {
            if (fromAutoCycle)
            {
                TriggerCount++;
                IdleService.BeginMove();
            }

            await _movementService.ExecuteMoveAsync(cts.Token);
        }
        catch (Exception ex)
        {
            Notice = $"滑鼠移動失敗：{ex.Message}"; // Phase 11 接上 LogService 後記錄
        }
        finally
        {
            if (fromAutoCycle)
            {
                IdleService.EndMove();
            }

            _moveCts = null;
            cts.Dispose();
            _moving = false;
        }
    }
```

4. `Pause()` 命令改為先取消移動：

```csharp
    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause()
    {
        _moveCts?.Cancel();
        IdleService.Pause();
    }
```

5. `Views/MainWindow.xaml` 狀態卡按鈕列（啟動/暫停之後）加：

```xml
                        <Button Content="立即執行一次" Command="{Binding MoveOnceCommand}"
                                Padding="16,8" Margin="8,0,0,0"/>
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 全綠（94 + 3 = 97）。另跑 `dotnet build -c Release`（0 error）。

- [ ] **Step 5: Commit**

```text
feat: 自動移動與立即執行一次 - VM 接線與取消流程

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

### Task 6: Phase 收尾

**Files:**
- Modify: `CHANGELOG.md`
- Modify: `docs/superpowers/plans/2026-08-20-mousepilot-master-plan.md`（Phase 3 → ✅ 完成，細部計畫文件欄填 `2026-08-20-phase3-mouse-movement.md`）

- [ ] **Step 1: CHANGELOG [Unreleased] 的「### 新增」補上**

```markdown
- 滑鼠自動微移（Phase 3）：左右/上下/隨機三模式、移動像素、回原位置（300ms）、螢幕邊界反向與多螢幕負座標支援；「立即執行一次」按鈕。
- 使用者輸入立即取消：真實輸入取消進行中移動；返回前雙重輸入檢查（鍵盤 lastInput 基準 + 游標位置）。
```

- [ ] **Step 2: Master Plan 進度表更新（僅 Phase 3 列）**

- [ ] **Step 3: 最終驗證**

```powershell
dotnet build -c Release
dotnet test tests/MousePilot.Tests
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

Expected: 全部成功；EXE 於 `bin\Release\net8.0-windows\win-x64\publish\`。

- [ ] **Step 4: Commit**

```text
docs: 更新 CHANGELOG 與進度總表 - Phase 3 完成

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

## Phase 3 完成定義

- [ ] build 0 error、測試全綠（預期 97）、publish 成功。
- [ ] 單元測試涵蓋：三模式位移、邊界反向、負座標、隨機目標合法、回原位流程、取消（token/鍵盤/滑鼠三路）、游標讀取失敗放棄、抑制窗涵蓋整個動作。
- [ ] **使用者實機手動驗證（規格 §34 案例 5~12 + §14，建議開始閒置暫調 10 秒）：**
  1. 「立即執行一次」：滑鼠立刻微移，開啟「回原位」時 0.3 秒後跳回原位。
  2. 左右/上下/隨機三模式各測一次（觀察方向與像素距離）。
  3. 放開 10 秒 → 滑鼠自動微移，之後每隔「後續移動間隔」動一次（案例 5）。
  4. 自動移動的瞬間動滑鼠/打字 → 立即停止且不再返回，重新計時（案例 4 強化版）。
  5. 把游標放到螢幕最右緣再「立即執行一次」→ 反向移動不出界（案例 10）。
  6. 多螢幕環境：游標移到副螢幕（含負座標側）執行 → 正常（案例 11、12）。
  7. 開「回原位置」後長時間掛機 → 游標不漂移。
