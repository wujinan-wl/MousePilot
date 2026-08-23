# MousePilot Phase 10：Single Instance 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** named Mutex 阻止第二實例；第二次啟動 `MousePilot.exe` 時**喚醒原實例開啟 Dashboard**（規格 §20 優先方案）；§30 結束流程補上步驟 8（Release Mutex）。

**Architecture:** 新 `SingleInstanceService`（named Mutex 判定 + named `EventWaitHandle` 跨程序喚醒：第二實例 Set、第一實例以 `ThreadPool.RegisterWaitForSingleObject` 監聽並發 `WakeRequested` 事件——不依賴視窗 handle，Tray 隱藏狀態也能喚醒；kernel object 名稱可注入，測試用唯一名稱、同程序雙 service 即可全覆蓋）。App 啟動順序關鍵修正：**marker crash 補救移到 mutex 取得之後**——第一實例套用游標時 marker 存在，若第二實例在 mutex 判定前就跑補救，會把第一實例套用中的游標恢復掉（`--restore-cursor` 明確參數則維持在最前，緊急恢復不受 mutex 阻擋——Phase 9 移交確認的接縫）。

**Tech Stack:** 既有 + `System.Threading.Mutex` / `EventWaitHandle`（BCL，無新 PInvoke）。

**Spec:** `docs/spec/mousepilot-spec.md`（§20、§30 步驟 8、§34 案例 28）；Master Plan Phase 10。

## 計畫決策（供使用者知悉，可否決）

1. **喚醒機制用 named EventWaitHandle**（非 named pipe / 自訂 window message）：BCL 原生、無視窗依賴（Tray 隱藏也可喚醒）、可單元測試；window message 需 HWND 廣播且 StartMinimized 下主視窗尚未建立。
2. **Mutex 用 per-user session 命名**（無 `Global\` 前綴）：不同 Windows 使用者各自可跑一份——符合「不需要 Administrator」與一般桌面工具慣例。
3. **前實例 crash 的 abandoned mutex 直接接手**（catch `AbandonedMutexException` 視為取得）——crash 後重啟必須能正常啟動。

## Global Constraints

- 喚醒後行為 = 既有 `ShowDashboard`（Show + 還原 Minimized + Activate）——涵蓋 Tray 隱藏狀態（Master Plan 風險項）。
- `WakeRequested` 在 threadpool thread 觸發——App 端必須 `Dispatcher.Invoke` 轉 UI thread。
- Mutex 執行緒親和：取得與釋放都在 UI thread（ctor/TryAcquire 於 OnStartup、Dispose 於 ExitApplication/OnExit）；`ReleaseMutex` 包 try/catch（錯誤緒釋放丟 `ApplicationException`——程序結束時 OS 自動釋放，吞掉安全）。
- 第二實例路徑：Signal → Shutdown——**不得**執行 marker 補救、不掛 exception hooks、不建 VM/Tray（副作用零）。
- kernel object 名稱經建構子注入；測試一律用 `Guid` 唯一名稱，絕不用 production 名稱（避免與真的在跑的 MousePilot 互撞）。
- TDD；綠了才 commit；commit 用 `$env:TEMP` 暫存檔 + `git commit -F`（禁 here-string），繁中+前綴+`Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`，commit 後 `git log -1 --format=%B` 驗證；禁止對 docs/ 或非任務檔案 git 還原。現況基準 242 綠。

---

### Task 1: SingleInstanceService（TDD）

**Files:**
- Create: `Services/SingleInstanceService.cs`
- Test: `tests/MousePilot.Tests/SingleInstanceServiceTests.cs`（新，6）

**Interfaces:**
- Produces（Task 2 依賴，逐字）: `class SingleInstanceService : IDisposable`：建構子 `(string? name = null)`（default `"MousePilot-SingleInstance"`）；`bool TryAcquire()`（true=第一實例；同時開始監聽喚醒）；`event Action? WakeRequested`（threadpool thread）；`void SignalFirstInstance()`（第二實例用）；`Dispose()`。

- [ ] **Step 1: 寫失敗測試（`tests/MousePilot.Tests/SingleInstanceServiceTests.cs`）**

```csharp
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
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 編譯失敗（SingleInstanceService 不存在）。

- [ ] **Step 3: 實作（`Services/SingleInstanceService.cs`）**

> **Review 修正註記**：原計畫在呼叫緒直接 `WaitOne(0)` 判定——經實測 Win32 named Mutex 擁有權以執行緒為單位且**同緒可重入**（即使不同 handle），同緒的第二個 service 也會取得成功（計畫自帶測試必紅）。改為專屬背景執行緒持有 mutex（阻塞至 Dispose 釋放），順帶修正 ReleaseMutex 執行緒親和問題。以下為實際落地版本。

```csharp
using System.Threading;

namespace MousePilot.Services;

/// <summary>
/// 單一實例（規格 §20）：named Mutex 判定 + named EventWaitHandle 跨程序喚醒。
/// 喚醒不依賴視窗 handle——Tray 隱藏狀態也能喚醒；WakeRequested 在 threadpool thread 觸發，UI 端需自行轉 Dispatcher。
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private readonly string _baseName;
    private readonly EventWaitHandle _wakeEvent;
    private readonly ManualResetEventSlim _releaseSignal = new(initialState: false);
    private RegisteredWaitHandle? _waitRegistration;
    private Thread? _ownerThread;
    private bool _owned;

    public SingleInstanceService(string? name = null)
    {
        _baseName = name ?? "MousePilot-SingleInstance";
        _wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _baseName + "-wake");
    }

    public event Action? WakeRequested;

    /// <summary>
    /// true = 本程序為第一實例（取得所有權並開始監聽喚醒訊號）。
    /// 注意：Win32 named Mutex 的擁有權以「執行緒」為單位（同執行緒對同一 named mutex 可重入取得，
    /// 即使透過不同的 Mutex handle）。因此判定與持有動作固定綁在專屬背景執行緒上執行，
    /// 該執行緒直到 Dispose() 才釋放 mutex 並結束——避免呼叫端執行緒重入造成誤判「已取得」。
    /// </summary>
    public bool TryAcquire()
    {
        using var acquiredSignal = new ManualResetEventSlim(false);
        var acquired = false;

        _ownerThread = new Thread(() =>
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
                    // 非取得緒釋放——程序結束時 OS 自動釋放，安全忽略
                }
            }
        })
        {
            IsBackground = true,
            Name = "SingleInstanceService-Owner",
        };
        _ownerThread.Start();
        acquiredSignal.Wait();

        _owned = acquired;

        if (_owned)
        {
            _waitRegistration = ThreadPool.RegisterWaitForSingleObject(
                _wakeEvent, (_, _) => WakeRequested?.Invoke(), null, Timeout.Infinite, executeOnlyOnce: false);
        }

        return _owned;
    }

    /// <summary>第二實例呼叫：通知第一實例開啟 Dashboard。</summary>
    public void SignalFirstInstance() => _wakeEvent.Set();

    public void Dispose()
    {
        _waitRegistration?.Unregister(null);
        _waitRegistration = null;

        if (_owned)
        {
            _releaseSignal.Set();
            _ownerThread?.Join();
            _owned = false;
        }

        _wakeEvent.Dispose();
        _releaseSignal.Dispose();
    }
}
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 全綠（242 + 6 = 248）。`dotnet build -c Release` 0 警告。

- [ ] **Step 5: Commit**

```text
feat: SingleInstanceService - Mutex 判定與跨程序喚醒

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

### Task 2: App 整線（第二實例讓路 + 喚醒 + §30 步驟 8）

**Files:**
- Modify: `App.xaml.cs`

**Interfaces:**
- Consumes: `SingleInstanceService`（Task 1）、既有 `ShowDashboard`。

- [ ] **Step 1: 實作（`App.xaml.cs`）**

1. 欄位 `private SingleInstanceService? _singleInstance;`。
2. `OnStartup` 重排（關鍵時序）：`--restore-cursor` 分支**維持最前**（緊急恢復不受 mutex 阻擋）；其後立刻插入 mutex 判定；**marker 補救與 exception hooks 移到取得 mutex 之後**（原因見計畫 Architecture——第二實例不得動第一實例套用中的游標）：

```csharp
        _cursorService = new CursorService();

        if (e.Args.Contains("--restore-cursor"))
        {
            _cursorService.Restore(); // 緊急補救參數：不受單一實例限制
            Shutdown();
            return;
        }

        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.TryAcquire())
        {
            _singleInstance.SignalFirstInstance(); // 通知原實例開啟 Dashboard（規格 §20）
            Shutdown();
            return; // 第二實例：零副作用讓路（不補救 marker、不掛 hooks、不建 VM）
        }

        if (_cursorService.HasPendingRestore)
        {
            _cursorService.Restore(); // 上次未正常恢復（crash）——先補救再繼續啟動
        }

        // 未處理例外最小 hook（Phase 11 才有完整 handler）：只恢復游標，不吞例外
        DispatcherUnhandledException += (_, _) => _cursorService?.Restore();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => _cursorService?.Restore();
        SessionEnding += (_, _) => _cursorService?.Restore(); // Windows 登出/關機
```

3. `OnStartup` 內 tray wiring 區之後（或緊接 `vm` 建立後）加喚醒訂閱：

```csharp
        _singleInstance.WakeRequested += () => Dispatcher.Invoke(ShowDashboard); // threadpool → UI thread
```

4. `ExitApplication`（§30 步驟 8）：

```csharp
        _exiting = true;
        _mainViewModel?.Dispose();      // 1~4：取消進行中移動、解除快捷鍵、停止輪詢 timer
        _cursorService?.Dispose();      // 5：恢復游標（已套用才動作）
        _tray?.Dispose();               // 6：系統匣圖示
        _mainViewModel?.SaveSettings(); // 7：保存設定
        _singleInstance?.Dispose();     // 8：釋放 Mutex
        Shutdown();                     // 9：關閉程式
```

5. `OnExit` 保險路徑 `_cursorService?.Dispose();` 後加 `_singleInstance?.Dispose();`。

- [ ] **Step 2: Build + 測試 + 雙實例冒煙**

Run: `dotnet build -c Release`（0 警告）、`dotnet test tests/MousePilot.Tests`（248 綠）。
雙實例冒煙（PowerShell）：
1. 背景啟動 exe（實例 A）→ `Start-Sleep 3` → 確認 A 存活。
2. 啟動第二份（實例 B）→ 等待 B process 結束（`WaitForExit`，逾時 10 秒）→ 確認 B 已結束且 A 仍存活。
3. `Stop-Process` A。
回報三步結果。

- [ ] **Step 3: Commit**

```text
feat: 單一實例整線 - 第二實例讓路喚醒與 Mutex 釋放順序

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

### Task 3: Phase 收尾

**Files:**
- Modify: `CHANGELOG.md`、`docs/superpowers/plans/2026-08-20-mousepilot-master-plan.md`

- [ ] **Step 1: CHANGELOG [Unreleased]「### 新增」補上**

```markdown
- 單一實例（Phase 10）：named Mutex 阻止多開；第二次啟動自動喚醒原實例開啟 Dashboard（Tray 隱藏狀態亦可）；crash 後 abandoned mutex 自動接手；結束流程補上釋放 Mutex（§30 步驟 8）。
```

- [ ] **Step 2: Master Plan 更新**：Phase 10 列 ✅ 完成、細部計畫文件欄 `2026-08-24-phase10-single-instance.md`。只動 Phase 10 列。

- [ ] **Step 3: 最終驗證**：`dotnet build -c Release` → `dotnet test tests/MousePilot.Tests`（248）→ `dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true`。

- [ ] **Step 4: Commit**

```text
docs: 更新 CHANGELOG 與進度總表 - Phase 10 完成

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

## Phase 10 完成定義

- [ ] build 0 error、測試全綠（預期 248）、publish 成功、雙實例冒煙通過。
- [ ] 單元測試涵蓋：取得/衝突/喚醒送達/釋放後再取得/abandoned 接手/未持有者 Dispose 隔離。
- [ ] **使用者實機手動驗證（§34 案例 28）：**
  1. 啟動 MousePilot（縮在 Tray）→ 再雙擊 `MousePilot.exe` → **不會多開**，原實例 Dashboard 自動跳出並取得焦點。
  2. Dashboard 開著時再啟動第二份 → 視窗還原/前景。
  3. 工作管理員強殺後重新啟動 → 正常啟動（abandoned mutex 接手）。
  4. 套用自訂游標狀態下啟動第二份 → 游標**不變**（第二實例不誤觸補救）、Dashboard 跳出。
