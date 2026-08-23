# MousePilot Phase 11：Exception Handling / Log 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** LogService（`%AppData%\MousePilot\Logs\mousepilot.log`、5MB rotate、保留 3 份歸檔）；全域未處理例外 handler 收斂為單一 `EmergencyShutdown`（記 log → 恢復游標 → 保存設定 → Tray.Dispose，防 ghost icon；不吞例外）；清償 Master Plan Phase 11 全部 15 條移交（a~m + backlog）。

**Architecture:** `LogService` 純檔案邏輯（目錄/clock 注入、寫入失敗靜默——log 不得反噬）；VM 以可選 `LogService?` 注入（null = 不記錄，測試零副作用）；`MoveResult` enum 取代 bare bool 讓 VM 能區分「取消/Win32 失敗/保守放棄」並以連續失敗 latch 進 `MonitorStatus.Error`（紅點，成功或重新啟動解除）；tray balloon 補「tray-only 狀態 Notice 不可見」缺口（App 統一接線：視窗不可見時 Notice → balloon）。

**Tech Stack:** 既有。無新相依。

**Spec:** `docs/spec/mousepilot-spec.md`（§21、§28、§29 log 項目、§34 案例 31）；Master Plan Phase 11 移交 (a)~(m) + backlog。

## 計畫決策（供使用者知悉，可否決）

1. **未處理例外不吞**（`e.Handled` 不設 true）：EmergencyShutdown 清理後讓程序終止——半死狀態繼續跑比乾淨 crash 危險；crash 後游標已恢復、設定已存、tray 已清。
2. **Error 狀態 latch**：滑鼠移動連續失敗 ≥3 次 → 紅點 Error（覆蓋輪詢狀態顯示）；下次成功移動或使用者按「啟動」解除。
3. **log 保留**：現行檔 + 3 份歸檔（共 4 份，落在規格 3~5 範圍）。
4. **Notice → balloon**：App 層統一接線（視窗不可見時每次 Notice 變更彈 balloon 3 秒），一次涵蓋 F10/hotkey/套用失敗等所有 tray-only 回饋（P6 移交 (b)）。

## Global Constraints

- **log 紀律**：不得對每次 mouse move 寫 log（Success 不記；ConservativeAbort 記 Info、Win32Failure 記 Error）；LogService 所有檔案操作自我 try/catch 靜默；handler 每一步各自 try/catch 到底（handler 內再拋例外 = 本 phase 最大風險）。
- **時序不變式（移交 m）**：exception hooks 掛載必須維持在 mutex 取得**之後**。
- 測試絕不觸真實 %AppData%/Registry/Win32：LogService 測試全用 temp 目錄注入；VM 測試 log 參數 default null（`_log?.` null-safe）；helper 既有 fake 紀律不變。
- 記錄項目（規格）：程式啟動、程式結束、設定載入錯誤、Cursor 套用錯誤、Registry 錯誤、Global Hotkey 錯誤、未處理例外。
- TDD；綠了才 commit；commit 用 `$env:TEMP` 暫存檔 + `git commit -F`（禁 here-string、UTF8 無 BOM），繁中+前綴+`Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`，`git log -1 --format=%B` 驗證；禁止對 docs/ 或非任務檔案 git 還原。現況基準 249 綠。

---

### Task 1: LogService（TDD）

**Files:**
- Create: `Services/LogService.cs`
- Test: `tests/MousePilot.Tests/LogServiceTests.cs`（新，6）

**Interfaces:**
- Produces（後續 task 依賴，逐字）: `class LogService`：建構子 `(string? logDir = null, long maxBytes = 5 * 1024 * 1024, int keepArchives = 3, Func<DateTime>? clock = null)`（default dir `%AppData%\MousePilot\Logs`）；`void Info(string message)`；`void Error(string message, Exception? ex = null)`；`string LogFilePath { get; }`。執行緒安全（lock）。

- [ ] **Step 1: 寫失敗測試（`tests/MousePilot.Tests/LogServiceTests.cs`）**

```csharp
using MousePilot.Services;

namespace MousePilot.Tests;

public sealed class LogServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "MousePilotLogTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private LogService Create(long maxBytes = 5 * 1024 * 1024, int keepArchives = 3)
        => new(_dir, maxBytes, keepArchives, () => new DateTime(2026, 8, 24, 12, 30, 45, 678));

    [Fact]
    public void 寫入含時間戳與等級()
    {
        Create().Info("程式啟動");
        var line = File.ReadAllLines(Path.Combine(_dir, "mousepilot.log")).Single();
        Assert.Equal("2026-08-24 12:30:45.678 [INFO] 程式啟動", line);
    }

    [Fact]
    public void Error含例外型別與訊息()
    {
        Create().Error("套用失敗", new InvalidOperationException("boom"));
        var text = File.ReadAllText(Path.Combine(_dir, "mousepilot.log"));
        Assert.Contains("[ERROR] 套用失敗", text);
        Assert.Contains("InvalidOperationException: boom", text);
    }

    [Fact]
    public void 超過大小觸發輪替鏈()
    {
        var log = Create(maxBytes: 100);
        log.Info(new string('a', 120)); // 第一筆寫入現行檔（寫入前空檔不輪替）
        log.Info("第二筆");             // 寫入前現行檔已超標 → 輪替

        Assert.True(File.Exists(Path.Combine(_dir, "mousepilot.1.log")));
        var current = File.ReadAllText(Path.Combine(_dir, "mousepilot.log"));
        Assert.Contains("第二筆", current);
        Assert.DoesNotContain("aaa", current);
    }

    [Fact]
    public void 保留數上限刪除最舊()
    {
        var log = Create(maxBytes: 10, keepArchives: 2);
        for (var i = 1; i <= 5; i++)
        {
            log.Info($"訊息{i}-{new string('x', 20)}"); // 每筆都觸發輪替
        }

        Assert.True(File.Exists(Path.Combine(_dir, "mousepilot.1.log")));
        Assert.True(File.Exists(Path.Combine(_dir, "mousepilot.2.log")));
        Assert.False(File.Exists(Path.Combine(_dir, "mousepilot.3.log"))); // keepArchives=2 → 不得有第 3 份
    }

    [Fact]
    public void 目錄自動建立()
    {
        var nested = Path.Combine(_dir, "deep", "logs");
        new LogService(nested, clock: () => DateTime.Now).Info("x");
        Assert.True(File.Exists(Path.Combine(nested, "mousepilot.log")));
    }

    [Fact]
    public void 寫入失敗靜默不拋()
    {
        var log = Create();
        using var blocker = new FileStream(
            Path.Combine(Directory.CreateDirectory(_dir).FullName, "mousepilot.log"),
            FileMode.Create, FileAccess.Write, FileShare.None); // 鎖住檔案

        var ex = Record.Exception(() => log.Info("被鎖住"));

        Assert.Null(ex); // log 不得反噬（規格：一般錯誤不得讓程式退出）
    }
}
```

- [ ] **Step 2: 執行測試確認紅**（編譯失敗）。

- [ ] **Step 3: 實作（`Services/LogService.cs`）**

```csharp
using System.IO;

namespace MousePilot.Services;

/// <summary>
/// 檔案 log（規格 §29）：mousepilot.log + 輪替歸檔 mousepilot.1.log ~ .N.log（1 最新）。
/// 所有檔案操作自我 try/catch 靜默——log 失敗絕不得影響程式（規格 §21）。執行緒安全。
/// </summary>
public class LogService
{
    private readonly object _gate = new();
    private readonly string _dir;
    private readonly long _maxBytes;
    private readonly int _keepArchives;
    private readonly Func<DateTime> _clock;

    public LogService(string? logDir = null, long maxBytes = 5 * 1024 * 1024, int keepArchives = 3, Func<DateTime>? clock = null)
    {
        _dir = logDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MousePilot", "Logs");
        _maxBytes = maxBytes;
        _keepArchives = keepArchives;
        _clock = clock ?? (() => DateTime.Now);
    }

    public string LogFilePath => Path.Combine(_dir, "mousepilot.log");

    public void Info(string message) => Write("INFO", message);

    public void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message}｜{ex.GetType().Name}: {ex.Message}");

    private void Write(string level, string message)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_dir);
                RotateIfNeeded();
                File.AppendAllText(LogFilePath,
                    $"{_clock():yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // 靜默：log 不得反噬
            }
        }
    }

    private void RotateIfNeeded()
    {
        var info = new FileInfo(LogFilePath);
        if (!info.Exists || info.Length < _maxBytes)
        {
            return;
        }

        var oldest = ArchivePath(_keepArchives);
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var i = _keepArchives - 1; i >= 1; i--)
        {
            if (File.Exists(ArchivePath(i)))
            {
                File.Move(ArchivePath(i), ArchivePath(i + 1));
            }
        }

        File.Move(LogFilePath, ArchivePath(1));
    }

    private string ArchivePath(int index) => Path.Combine(_dir, $"mousepilot.{index}.log");
}
```

- [ ] **Step 4: 測試全綠**（249 + 6 = 255）、`dotnet build -c Release` 0 警告。

- [ ] **Step 5: Commit**：`feat: LogService - 檔案 log 與大小輪替`

---

### Task 2: CursorService marker write-ahead + SingleInstanceService 強化（TDD；移交 j/l）

**Files:**
- Modify: `Services/CursorService.cs`、`Services/SingleInstanceService.cs`
- Test: `tests/MousePilot.Tests/CursorServiceTests.cs`（改 2 加 1）、`tests/MousePilot.Tests/SingleInstanceServiceTests.cs`（+2）

**Interfaces:** 無簽名變更（`TryAcquire` 加重複呼叫守衛丟 `InvalidOperationException` 屬新行為）。

- [ ] **Step 1: 寫/改失敗測試**

`CursorServiceTests.cs`：
1. 既有 `載入失敗時套用失敗不寫marker` 與 `替換失敗時銷毀handle不寫marker` 的 marker 斷言**維持不變**（未曾套用的失敗不得殘留 marker——write-ahead 後由「失敗即清」保證）。
2. 新增：

```csharp
    [Fact]
    public void 已套用後再套用失敗時marker保留()
    {
        var setResults = new Queue<bool>(new[] { true, false });
        var svc = Create(set: _ => setResults.Dequeue());
        Assert.True(svc.Apply(CurPath));   // 第一次成功——游標已被替換

        Assert.False(svc.Apply(CurPath));  // 第二次失敗

        Assert.True(svc.HasPendingRestore); // 系統游標仍是自訂的——marker 必須留著
        Assert.True(svc.IsApplied);
    }
```

`SingleInstanceServiceTests.cs` 新增：

```csharp
    [Fact]
    public void 名稱被其他型別占用時fail_open取得()
    {
        var name = UniqueName();
        using var squatter = new EventWaitHandle(false, EventResetMode.ManualReset, name); // 占用 mutex 名稱

        using var svc = new SingleInstanceService(name);

        Assert.True(svc.TryAcquire()); // kernel object 例外 → fail-open：寧可多開也不可啟動即 crash（規格 §21）
    }

    [Fact]
    public void TryAcquire重複呼叫丟例外()
    {
        using var svc = new SingleInstanceService(UniqueName());
        Assert.True(svc.TryAcquire());
        Assert.Throws<InvalidOperationException>(() => svc.TryAcquire());
    }
```

- [ ] **Step 2: 確認紅。**

- [ ] **Step 3: 實作**

`CursorService.Apply` 改 write-ahead（移交 j——封死「替換成功後、marker 寫入前被強殺」縫隙）：

```csharp
    public virtual bool Apply(string curFilePath)
    {
        if (!File.Exists(curFilePath))
        {
            return false;
        }

        var handle = _loadCursorFromFile(curFilePath);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        // write-ahead：先立 marker 再替換——若替換成功瞬間被強殺，下次啟動仍會補救。
        // 失敗時只在「先前未套用」才清 marker（已套用中的失敗，系統游標仍是自訂的，marker 必須留）。
        var wasApplied = IsApplied;
        WriteMarker();
        if (!_setSystemCursor(handle))
        {
            _destroyCursor(handle);
            if (!wasApplied)
            {
                DeleteMarker();
            }

            return false;
        }

        IsApplied = true;
        return true;
    }
```

`SingleInstanceService`（四件，移交 l）：
1. `TryAcquire()` 開頭加：

```csharp
        if (_ownerThread is not null)
        {
            throw new InvalidOperationException("TryAcquire 只能呼叫一次（重複呼叫會令第一持有緒永久等待）。");
        }
```

2. 持有緒 lambda 的 `new Mutex` + `WaitOne` 整段包 fail-open：

```csharp
            try
            {
                using var mutex = new Mutex(initiallyOwned: false, _baseName);
                try
                {
                    acquired = mutex.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true; // 前實例 crash 未釋放——接手（計畫決策 3）
                }

                acquiredSignal.Set();

                if (acquired)
                {
                    _releaseSignal.Wait();
                    try
                    {
                        mutex.ReleaseMutex();
                    }
                    catch (ApplicationException)
                    {
                        // 防禦性——重設計後取得與釋放同緒，理論上不會發生
                    }
                }
            }
            catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException)
            {
                // kernel object 名稱被其他型別占用/ACL 拒絕：fail-open——寧可失去單一實例保證也不可啟動即 crash（規格 §21）
                acquired = true;
                acquiredSignal.Set();
                _releaseSignal.Wait();
            }
```

（注意 `acquiredSignal.Set()` 在兩個路徑都必須恰好呼叫一次；fail-open 路徑也要等 `_releaseSignal` 讓 Dispose 的 Join 收斂。）
3. `Dispose()` 的 `_waitRegistration?.Unregister(null)` 改 `_waitRegistration?.Unregister(_wakeEvent);`。
4. ctor 的 `new EventWaitHandle(...)` 亦包 try/catch？——**不做**（wake event 名稱衝突機率同 mutex，但 ctor 失敗無法 fail-open 出合理狀態；維持現狀，由 App 的 EmergencyShutdown 涵蓋）。在報告註明此裁定。

- [ ] **Step 4: 測試全綠**（255 + 3 = 258；既有 2 個 marker 測試不變仍綠）、build 0 警告。

- [ ] **Step 5: Commit**：`fix: marker write-ahead 與單一實例 fail-open 強化`

---

### Task 3: MoveResult enum + Error 狀態 latch + VM log 注入（TDD；移交 a/b/c/d）

**Files:**
- Modify: `Services/MouseMovementService.cs`（`Task<bool>` → `Task<MoveResult>`）、`Models/MonitorStatus.cs`（+`Error`）、`ViewModels/MainViewModel.cs`、`Views/MainWindow.xaml`（紅點 trigger）、`App.xaml`（`StatusErrorBrush` 資源，紅 `#DC2626`）
- Test: `tests/MousePilot.Tests/MouseMovementServiceTests.cs`（回傳值斷言同步改）、`tests/MousePilot.Tests/MainViewModelTests.cs`（+4）

**Interfaces:**
- Produces: `enum MoveResult { Success, Cancelled, ConservativeAbort, Win32Failure }`（Models/ 或 Services/ 依 MouseMovementService 檔內慣例）；`MouseMovementService.ExecuteMoveAsync : Task<MoveResult>`——對應：ct 取消→`Cancelled`；游標讀取失敗/起點出界/雙重檢查放棄→`ConservativeAbort`；`sendMove` 回 false→`Win32Failure`；其餘→`Success`（`correctPosition` 為 ±1px best-effort 校正，回傳值不影響分級——final review 對齊實作）。`MainViewModel` 第 11 參數 `LogService? logService = null`；`MonitorStatus.Error`；連續 `Win32Failure` ≥3 → Status latch `Error`（`StatusText` = 「錯誤（滑鼠移動失敗）」），下次 `Success`、`StartCommand` 或 **`PauseCommand`**（暫停即解除——否則 Status 永遠 Error、CanStart 永久 false 卡死 UI；review 修正）解除；`_moving` 防重入丟棄記 Info；`WasCorrupt` 記 Error（設定載入錯誤）。

- [ ] **Step 1: 寫失敗測試**

`MainViewModelTests.cs` 新增（helper `CreateVmWithStartup` 加可選參數 `LogService? logService = null` 傳入第 11 參數；`CreateVmWithReturn`/`CreateVmWithCursor` 不需動——default null 不記錄）：

```csharp
    [Fact]
    public void 連續移動失敗三次進入錯誤狀態()
    {
        // 用 CreateVmWithReturn 模式建 VM，但 sendMove 固定 false → Win32Failure
        // （依既有 helper 結構調整：movementServiceFactory 的 sendMove: (_, _) => false）
        var vm = CreateVmWithFailingMove();
        vm.StartCommand.Execute(null);

        vm.MoveOnceCommand.Execute(null); // 1
        vm.MoveOnceCommand.Execute(null); // 2
        Assert.NotEqual(MonitorStatus.Error, vm.Status);
        vm.MoveOnceCommand.Execute(null); // 3

        Assert.Equal(MonitorStatus.Error, vm.Status);
        Assert.Contains("錯誤", vm.StatusText);
    }

    [Fact]
    public void 錯誤狀態由重新啟動解除()
    {
        var vm = CreateVmWithFailingMove();
        vm.StartCommand.Execute(null);
        for (var i = 0; i < 3; i++) { vm.MoveOnceCommand.Execute(null); }
        Assert.Equal(MonitorStatus.Error, vm.Status);

        vm.PauseCommand.Execute(null);
        vm.StartCommand.Execute(null); // 重新啟動 → latch 解除

        Assert.NotEqual(MonitorStatus.Error, vm.Status);
    }

    [Fact]
    public void 移動失敗寫入log()
    {
        var dir = Path.Combine(_dir, "logs");
        var log = new LogService(dir, clock: () => DateTime.Now);
        var vm = CreateVmWithFailingMove(log);
        vm.StartCommand.Execute(null);

        vm.MoveOnceCommand.Execute(null);

        Assert.Contains("[ERROR]", File.ReadAllText(Path.Combine(dir, "mousepilot.log")));
    }

    [Fact]
    public void 設定損毀寫入log()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ 不是 JSON");
        var dir = Path.Combine(_dir, "logs");
        var log = new LogService(dir, clock: () => DateTime.Now);

        _ = CreateVmWithStartup(new NoOpStartupService(), logService: log);

        Assert.Contains("設定", File.ReadAllText(Path.Combine(dir, "mousepilot.log")));
    }
```

（`CreateVmWithFailingMove(LogService? log = null)` 為新私有 helper：比照 `CreateVmWithReturn` 全注入，`sendMove: (_, _) => false`、`logService: log`、其餘 fake 同既有紀律。`MoveOnceAsync` 為 async void command——若命令執行為同步完成（所有注入 delay 皆 `Task.CompletedTask`）可直接斷言；以既有 `MoveRecorder` 測試同款模式為準。）

`MouseMovementServiceTests.cs`：所有 `Assert.True(await ...ExecuteMoveAsync(...))` 類斷言改 `Assert.Equal(MoveResult.Success, ...)`，失敗情境依對應 enum 值改斷言（取消→`Cancelled`、雙重檢查放棄→`ConservativeAbort`、sendMove false→`Win32Failure`——逐一比對既有測試意圖）。

- [ ] **Step 2: 確認紅。**
- [ ] **Step 3: 實作**（`MonitorStatus` 加 `Error`；`MouseMovementService` 改回傳 enum；VM：`_log` 欄位＋第 11 參數、`_consecutiveMoveFailures` 計數與 latch（`OnTicked` 中 `Status = _errorLatched ? MonitorStatus.Error : result.State;`、`StatusText` 對應）、`ExecuteMoveGuardedAsync` 依 `MoveResult` 分支記 log/計數、`StartMonitoring` 清 latch、`_moving` 丟棄記 Info、ctor `WasCorrupt` 分支加 `_log?.Error(...)`；XAML：`App.xaml` 加 `StatusErrorBrush`（#DC2626）、`MainWindow.xaml` Ellipse 加 `Error` DataTrigger；規格 §28。）
- [ ] **Step 4: 測試全綠**（258 + 4 = 262，既有移動測試同步改）、build 0 警告。
- [ ] **Step 5: Commit**：`feat: 移動結果分級與錯誤狀態紅點及 log 注入`

---

### Task 4: Hotkey 錯誤碼區分 + Clamp 刷新包裝屬性（TDD；移交 f/h）

**Files:**
- Modify: `Services/HotkeyService.cs`、`ViewModels/MainViewModel.cs`、`Views/MainWindow.xaml`（數值 TextBox rebind）
- Test: `tests/MousePilot.Tests/HotkeyServiceTests.cs`（+1）、`tests/MousePilot.Tests/MainViewModelTests.cs`（+3）

**Interfaces:**
- Produces: `HotkeyService`：建構子加第 3 可選參數 `Func<int>? lastErrorFn = null`（default `Marshal.GetLastWin32Error`）；`public int LastWin32Error { get; private set; }`（Register 失敗時更新）。`MainViewModel`：`IdleStartSecondsInput`/`MovementIntervalSecondsInput`/`MovementPixelsInput` int 包裝屬性（setter 寫 Settings + OnPropertyChanged；`StartMonitoring` 的 Clamp 後對三者發 PropertyChanged）；hotkey 重複檢查改比較 `HotkeyParser.Parse` 結果（大小寫/順序不同的等價組合也擋）；註冊失敗訊息依 `LastWin32Error`：1409 → 「已被其他程式占用」、其他 → 「註冊失敗（Win32 錯誤 N）」，並記 log。

- [ ] **Step 1: 寫失敗測試**

`HotkeyServiceTests.cs`（比照既有注入模式）：

```csharp
    [Fact]
    public void 註冊失敗記錄Win32錯誤碼()
    {
        var svc = new HotkeyService(registerFn: (_, _, _) => false, unregisterFn: _ => true, lastErrorFn: () => 1409);
        Assert.False(svc.Register(1, HotkeyParser.Parse("Ctrl+Alt+F9")!.Value));
        Assert.Equal(1409, svc.LastWin32Error);
    }
```

`MainViewModelTests.cs`：

```csharp
    [Fact]
    public void 啟動時夾制數值並刷新UI()
    {
        var vm = CreateVmWithStartup(new NoOpStartupService());
        vm.IdleStartSecondsInput = 0; // 低於下限（TextBox 直改）
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.StartCommand.Execute(null);

        Assert.Equal(5, vm.IdleStartSecondsInput); // Clamp 後包裝屬性已刷新（移交 f）
        Assert.Contains(nameof(MainViewModel.IdleStartSecondsInput), raised);
    }

    [Fact]
    public void 快捷鍵等價組合視為重複()
    {
        var hotkeys = new HotkeyHarness();
        var vm = CreateVmWithStartup(new NoOpStartupService(), hotkeys.Service);
        // RestoreCursorHotkey 預設 Ctrl+Alt+F10——用不同字串寫法的等價組合設定 Toggle
        vm.ToggleHotkeyText = "Alt+Ctrl+F10"; // Parse 後與 Ctrl+Alt+F10 等價（修飾鍵順序不同）

        Assert.Contains("重複", vm.Notice);
    }

    [Fact]
    public void 快捷鍵註冊失敗訊息區分錯誤碼()
    {
        var svc = new HotkeyService(registerFn: (_, _, _) => false, unregisterFn: _ => true, lastErrorFn: () => 5);
        var vm = CreateVmWithStartup(new NoOpStartupService(), svc);
        Assert.Contains("Win32 錯誤 5", vm.Notice); // 非 1409 不得謊稱「被占用」
    }
```

（`ToggleHotkeyText` 為既有包裝屬性名——**以檔內實際名稱為準**，若不同以實名替換；等價組合寫法 `Alt+Ctrl+F10` 若 `Validate` 先擋非正規順序則改用 Parse 結果可等價的合法寫法——實作時以現行 Parser 行為為準，測試意圖是「Parse 相等即重複」。）

- [ ] **Step 2: 確認紅。**
- [ ] **Step 3: 實作**（HotkeyService 第 3 參數與 LastWin32Error；VM 三個 Input 包裝屬性 + `StartMonitoring` Clamp 後 `OnPropertyChanged` 三發；`MainWindow.xaml` 三個 TextBox `Settings.X` → `XInput` rebind；dup check `HotkeyParser.Parse(text)` vs `HotkeyParser.Parse(other)` 值比較；`RegisterHotkeyFromSettings` 失敗訊息依 LastWin32Error 分流 + `_log?.Error`。）
- [ ] **Step 4: 測試全綠**（262 + 4 = 266）、build 0 警告。
- [ ] **Step 5: Commit**：`feat: 快捷鍵錯誤碼區分與數值夾制 UI 刷新`

---

### Task 5: App EmergencyShutdown 收斂 + balloon 回饋 + 剩餘接線（移交 b/e/g/i/k + P6(b)）

**Files:**
- Modify: `App.xaml.cs`、`Services/TrayIconService.cs`、`ViewModels/MainViewModel.cs`（`NotifySystemRestored` + ApplyCursor 失敗清 enabled + 啟動/結束 log）
- Test: `tests/MousePilot.Tests/MainViewModelTests.cs`（+2）、`tests/MousePilot.Tests/TrayIconServiceTests.cs`（改名）

**Interfaces:**
- Produces: `TrayIconService.ShowInfo(string text)`（BalloonTip 3 秒；空字串忽略）；`MainViewModel.NotifySystemRestored()`（只刷 `CursorStatusText`，不動 `CustomCursorEnabled`——修「取消登出後 UI 不同步」，移交 i）；`ApplyCursor` 失敗 → `CustomCursorEnabled = false` + `SaveSettings()` + log（backlog k——防啟動 auto-apply 每次重試）；App `EmergencyShutdown(string source, Exception? ex)`。

- [ ] **Step 1: 寫失敗測試**

```csharp
    [Fact]
    public void 系統恢復通知只刷狀態不動設定()
    {
        var cursor = new FakeCursorService();
        var vm = CreateVmWithStartup(new NoOpStartupService(), cursorService: cursor);
        vm.Settings.ConfirmedCursorFile = @"C:\a.cur";
        vm.ApplyCursorCommand.Execute(null);

        cursor.Restore();            // 模擬 SessionEnding 直接恢復（不經 VM）
        vm.NotifySystemRestored();

        Assert.Equal("Windows 預設", vm.CursorStatusText);
        Assert.True(vm.Settings.CustomCursorEnabled); // 不動 enabled——下次登入自動套回（移交 i）
    }

    [Fact]
    public void 套用失敗清除啟用旗標()
    {
        var cursor = new FakeCursorService { ApplyResult = false };
        var vm = CreateVmWithStartup(new NoOpStartupService(), cursorService: cursor);
        vm.Settings.ConfirmedCursorFile = @"C:\gone.cur";
        vm.Settings.CustomCursorEnabled = true; // 模擬啟動 auto-apply 情境

        vm.ApplyCursorCommand.Execute(null);

        Assert.False(vm.Settings.CustomCursorEnabled); // 失敗不得留 true——否則每次啟動重試（backlog k）
    }
```

`TrayIconServiceTests.cs`：`游標三項於Phase9前停用` 改名 `游標三項已啟用`（內容不變）。

- [ ] **Step 2: 確認紅。**
- [ ] **Step 3: 實作**

1. VM：`NotifySystemRestored()` = `RefreshCursorStatusText();`；`ApplyCursor` 失敗分支加 `Settings.CustomCursorEnabled = false; SaveSettings(); _log?.Error(...)`；成功/恢復分支加 `_log?.Info(...)`；`SaveSettings` 失敗分支加 `_log?.Error(...)`。
2. `TrayIconService.ShowInfo(string text)`：`if (text.Length == 0) return; _notifyIcon.ShowBalloonTip(3000, "MousePilot", text, ToolTipIcon.Info);`。
3. App：
   - 建 `_logService = new LogService();`（mutex 取得之後——第二實例不記 log）；VM 建構傳 `logService: _logService`；啟動後 `_logService.Info("程式啟動");`、`ExitApplication` 加 `_logService?.Info("程式結束");`。
   - 三個 hook 收斂：

```csharp
        DispatcherUnhandledException += (_, args) => EmergencyShutdown("Dispatcher", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => EmergencyShutdown("AppDomain", args.ExceptionObject as Exception);
        SessionEnding += (_, _) =>
        {
            _logService?.Info("Session 結束（登出/關機）——恢復游標");
            _cursorService?.Restore();
            _mainViewModel?.NotifySystemRestored();
        };
```

```csharp
    /// <summary>未處理例外的最後防線（移交 b/e/g）：每步各自 try/catch 到底；不吞例外——清理後讓程序終止。</summary>
    private void EmergencyShutdown(string source, Exception? ex)
    {
        try { _logService?.Error($"未處理例外（{source}）", ex); } catch { /* 最後防線內不得再拋 */ }
        try { _cursorService?.Restore(); } catch { }
        try { _mainViewModel?.SaveSettings(); } catch { }
        try { _tray?.Dispose(); } catch { }
    }
```

（此處為專案唯一允許的裸 catch——最後防線自身絕不可拋，註解已載明理由。移交 (g)：HotkeyService WndProc 例外沿 Dispatcher 傳播，`DispatcherUnhandledException` 已涵蓋——在 EmergencyShutdown xml-doc 註記。）
   - balloon 接線（P6 移交 b + backlog k）：`OnViewModelPropertyChanged` 加：

```csharp
        if (e.PropertyName == nameof(MainViewModel.Notice)
            && _mainViewModel is { } vmn && vmn.Notice.Length > 0
            && MainWindow?.IsVisible != true)
        {
            _tray?.ShowInfo(vmn.Notice); // tray-only 狀態下的通知可見性（P6 移交 b）
        }
```

- [ ] **Step 4: 測試全綠**（266 + 2 = 268）、build 0 警告、啟動冒煙（4 秒存活 + log 檔出現「程式啟動”）。
- [ ] **Step 5: Commit**：`feat: EmergencyShutdown 收斂與 balloon 通知及全域 log 接線`

---

### Task 6: Phase 收尾

**Files:** `CHANGELOG.md`、`docs/superpowers/plans/2026-08-20-mousepilot-master-plan.md`

- [ ] **Step 1: CHANGELOG [Unreleased]「### 新增」**：

```markdown
- 例外處理與 Log（Phase 11）：LogService（5MB 輪替、保留 3 份歸檔、失敗靜默）；未處理例外統一 EmergencyShutdown（記 log→恢復游標→存設定→清 Tray，不吞例外）；滑鼠移動連續失敗紅色錯誤狀態；快捷鍵錯誤碼區分與等價組合查重；數值夾制後 UI 即時刷新；tray-only 狀態通知改 balloon 顯示；marker write-ahead 與單一實例 fail-open 強化。
```

- [ ] **Step 2: Master Plan**：Phase 11 列 ✅ 完成 + 細部計畫文件欄；Phase 11 章節移交 15 條逐條標記完成情況（一行註記即可）。
- [ ] **Step 3: 最終驗證**：build → test（268）→ publish。
- [ ] **Step 4: Commit**：`docs: 更新 CHANGELOG 與進度總表 - Phase 11 完成`

---

## Phase 11 完成定義

- [ ] build 0 error、測試全綠（預估 268，以無 FAIL 為準）、publish 成功。
- [ ] 移交 15 條全數清償或明確記錄去向。
- [ ] **使用者實機手動驗證（§34 案例 31 + §21）：**
  1. 啟動程式 → `%AppData%\MousePilot\Logs\mousepilot.log` 出現「程式啟動」；關閉 → 「程式結束」。
  2. 手動把 settings.json 改壞 → 啟動不 crash、提示備份、log 有記錄。
  3. 縮在 Tray 時按 F10 → **balloon 通知**「已恢復 Windows 游標」。
  4. 設定快捷鍵成與另一項等價的組合 → 提示重複。
  5. 閒置秒數輸入 0 → 按啟動 → 欄位即時變回 5。
  6. （可選）刪掉 confirmed-cursor.cur 且 customCursorEnabled=true → 啟動一次失敗後不再每次重試。
