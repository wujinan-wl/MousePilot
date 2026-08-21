# MousePilot Phase 4：System Tray 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 系統匣完整支援：tray icon + 右鍵選單（九項，游標三項停用佔位）、雙擊開 Dashboard、關閉視窗縮到系統匣（可設定）、啟動後最小化到系統匣（規格 §16 預設）、安全結束流程（規格 §30 現階段子集）。

**Architecture:** `TrayIconService`（WinForms `NotifyIcon` 薄包裝：只轉發事件、不含業務邏輯，選單結構與狀態切換可單元測試）；App.xaml.cs 為組合根（wire 事件到 VM 命令、視窗顯示/隱藏、安全結束）；`ShutdownMode` 改 `OnExplicitShutdown`。**技術決策（推翻 Master Plan 原傾向的純 PInvoke）**：WinForms NotifyIcon——(a) 自動處理 TaskbarCreated/Explorer 重啟重建（規格 §21 要求）；(b) `UseWindowsForms` 屬 .NET Desktop Runtime，self-contained EXE 本已包含，非新 NuGet 相依；(c) 省 ~200 行難測 PInvoke，穩定性優先（規格 §40-3）。

**Tech Stack:** 既有 + `<UseWindowsForms>true</UseWindowsForms>`（主專案與測試專案）。無新 NuGet。

**Spec:** `docs/spec/mousepilot-spec.md`（§13/§14/§16/§21/§30/§34 案例 25）；Master Plan Phase 4 章節。

## Global Constraints

- Tray 選單順序（規格 §13）：開啟 MousePilot｜—｜啟動｜暫停｜立即執行一次｜—｜啟用自訂游標｜停用自訂游標｜恢復 Windows 游標｜—｜設定｜結束。游標三項 `Enabled=false`（Phase 9 啟用）；「設定」暫開 Dashboard（設定就在 Dashboard 上）。
- 雙擊 tray icon → 開 Dashboard（規格 §13）。
- 關閉視窗：`MinimizeToTrayOnClose=true`（預設）→ 取消關閉、Hide；false → 執行完整安全結束（規格 §13「可在設定中決定」）。
- `StartMinimized=true`（預設）→ 啟動不顯示視窗、只有 tray（規格 §16 模式二預設）。
- 安全結束順序（規格 §30 現階段子集）：VM.Dispose（取消移動+停 timer）→ Tray.Dispose → SaveSettings → Shutdown；hotkey/cursor/mutex 步驟由 Phase 6/9/10 插入。
- `NotifyIcon.Text` 上限 63 字元——設定前必須截斷（超長會擲例外）。
- TrayIconService 內不得引用 `System.Windows`（避免 WPF/WinForms 型別衝突）；WinForms 型別不得外洩到 VM。
- 所有 unmanaged/WinForms 資源正確 Dispose（NotifyIcon 先設 `Visible=false` 再 Dispose 避免殘影；GetHicon 的 handle 用 `DestroyIcon` 釋放）。
- TDD：可測部分（選單結構/狀態切換/事件轉發/tooltip 截斷）先測試後實作；目前基準 98 綠。
- Commit 一律 `git commit -F <暫存檔>`（禁 here-string），繁中+前綴+`Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`，commit 後 `git log -1 --format=%B` 驗證。

---

### Task 1: TrayIconService（TDD）

**Files:**
- Modify: `MousePilot.csproj`、`tests/MousePilot.Tests/MousePilot.Tests.csproj`（各加一行 `<UseWindowsForms>true</UseWindowsForms>`，放在 `<UseWPF>true</UseWPF>` 之後）
- Modify: `Native/NativeMethods.cs`（新增 DestroyIcon）
- Create: `Services/TrayIconService.cs`
- Test: `tests/MousePilot.Tests/TrayIconServiceTests.cs`

**Interfaces:**
- Produces（Task 2 依賴，逐字）:
  - `sealed class TrayIconService : IDisposable`：建構子 `TrayIconService(bool visible = true)`；事件 `OpenRequested`/`StartRequested`/`PauseRequested`/`MoveOnceRequested`/`ExitRequested`（皆 `Action?`）；`void UpdateStatus(MonitorStatus status, string statusText)`；測試鉤 `ToolStripMenuItem? FindMenuItem(string text)`、`IReadOnlyList<string> MenuTexts`、`string TooltipText`。
  - `NativeMethods`：`internal static extern bool DestroyIcon(IntPtr hIcon)`。

- [ ] **Step 1: csproj 加 UseWindowsForms（兩個專案），先 build 確認無型別衝突**

```powershell
dotnet build -c Release
```

Expected: 0 error（既有程式碼無 `using System.Windows.Forms`，不會產生歧義）。若出現型別歧義錯誤，如實回報。

- [ ] **Step 2: 寫失敗測試**

`tests/MousePilot.Tests/TrayIconServiceTests.cs`：

```csharp
using MousePilot.Models;
using MousePilot.Services;

namespace MousePilot.Tests;

public class TrayIconServiceTests
{
    private static TrayIconService Create() => new(visible: false);

    [Fact]
    public void 選單結構符合規格順序()
    {
        using var tray = Create();
        Assert.Equal(
            new[]
            {
                "開啟 MousePilot", "-",
                "啟動", "暫停", "立即執行一次", "-",
                "啟用自訂游標", "停用自訂游標", "恢復 Windows 游標", "-",
                "設定", "結束",
            },
            tray.MenuTexts);
    }

    [Fact]
    public void 游標三項於Phase9前停用()
    {
        using var tray = Create();
        Assert.False(tray.FindMenuItem("啟用自訂游標")!.Enabled);
        Assert.False(tray.FindMenuItem("停用自訂游標")!.Enabled);
        Assert.False(tray.FindMenuItem("恢復 Windows 游標")!.Enabled);
    }

    [Fact]
    public void UpdateStatus切換啟動暫停可用性()
    {
        using var tray = Create();
        tray.UpdateStatus(MonitorStatus.Paused, "已暫停");
        Assert.True(tray.FindMenuItem("啟動")!.Enabled);
        Assert.False(tray.FindMenuItem("暫停")!.Enabled);

        tray.UpdateStatus(MonitorStatus.Monitoring, "監控中");
        Assert.False(tray.FindMenuItem("啟動")!.Enabled);
        Assert.True(tray.FindMenuItem("暫停")!.Enabled);
    }

    [Fact]
    public void 選單點擊觸發對應事件()
    {
        using var tray = Create();
        var log = new List<string>();
        tray.OpenRequested += () => log.Add("open");
        tray.StartRequested += () => log.Add("start");
        tray.PauseRequested += () => log.Add("pause");
        tray.MoveOnceRequested += () => log.Add("move");
        tray.ExitRequested += () => log.Add("exit");

        tray.FindMenuItem("開啟 MousePilot")!.PerformClick();
        tray.FindMenuItem("啟動")!.PerformClick();
        tray.FindMenuItem("暫停")!.PerformClick();
        tray.FindMenuItem("立即執行一次")!.PerformClick();
        tray.FindMenuItem("設定")!.PerformClick();   // 設定 → 開 Dashboard
        tray.FindMenuItem("結束")!.PerformClick();

        Assert.Equal(new[] { "open", "start", "pause", "move", "open", "exit" }, log);
    }

    [Fact]
    public void Tooltip過長時截斷為63字元()
    {
        using var tray = Create();
        tray.UpdateStatus(MonitorStatus.Monitoring, new string('監', 100));
        Assert.Equal(63, tray.TooltipText.Length);
        Assert.StartsWith("MousePilot", tray.TooltipText);
    }
}
```

- [ ] **Step 3: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 編譯失敗（`TrayIconService` 不存在）。若 WinForms 元件在測試執行緒擲 `ThreadStateException` 之類錯誤，回報 BLOCKED（可能需要 STA 處理），不要自行大改。

- [ ] **Step 4: 實作**

`Native/NativeMethods.cs` 新增（放在 `SetCursorPos` 宣告之後）：

```csharp
    /// <summary>釋放 GetHicon 產生的 icon handle（Bitmap.GetHicon 不會自動釋放）。</summary>
    [DllImport("user32.dll")]
    internal static extern bool DestroyIcon(IntPtr hIcon);
```

`Services/TrayIconService.cs`：

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using MousePilot.Models;
using MousePilot.Native;

namespace MousePilot.Services;

/// <summary>
/// 系統匣圖示與右鍵選單（規格 §13）。薄包裝：不含業務邏輯、只轉發事件，
/// 讓選單結構與狀態切換可單元測試。游標三項選單由 Phase 9 啟用。
/// WinForms NotifyIcon 內建 TaskbarCreated（Explorer 重啟）重建行為（規格 §21）。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    public event Action? OpenRequested;
    public event Action? StartRequested;
    public event Action? PauseRequested;
    public event Action? MoveOnceRequested;
    public event Action? ExitRequested;

    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _startItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly Icon _icon;

    public TrayIconService(bool visible = true)
    {
        _menu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("開啟 MousePilot");
        openItem.Click += (_, _) => OpenRequested?.Invoke();
        _startItem = new ToolStripMenuItem("啟動");
        _startItem.Click += (_, _) => StartRequested?.Invoke();
        _pauseItem = new ToolStripMenuItem("暫停");
        _pauseItem.Click += (_, _) => PauseRequested?.Invoke();
        var moveOnceItem = new ToolStripMenuItem("立即執行一次");
        moveOnceItem.Click += (_, _) => MoveOnceRequested?.Invoke();
        var enableCursorItem = new ToolStripMenuItem("啟用自訂游標") { Enabled = false };   // Phase 9
        var disableCursorItem = new ToolStripMenuItem("停用自訂游標") { Enabled = false };  // Phase 9
        var restoreCursorItem = new ToolStripMenuItem("恢復 Windows 游標") { Enabled = false }; // Phase 9
        var settingsItem = new ToolStripMenuItem("設定");
        settingsItem.Click += (_, _) => OpenRequested?.Invoke(); // 設定就在 Dashboard 上
        var exitItem = new ToolStripMenuItem("結束");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        _menu.Items.AddRange(new ToolStripItem[]
        {
            openItem, new ToolStripSeparator(),
            _startItem, _pauseItem, moveOnceItem, new ToolStripSeparator(),
            enableCursorItem, disableCursorItem, restoreCursorItem, new ToolStripSeparator(),
            settingsItem, exitItem,
        });

        _icon = CreateIcon();
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            ContextMenuStrip = _menu,
            Text = "MousePilot",
            Visible = visible,
        };
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public string TooltipText => _notifyIcon.Text;

    /// <summary>依監控狀態更新選單可用性與 tooltip（NotifyIcon.Text 上限 63 字元，超長會擲例外，先截斷）。</summary>
    public void UpdateStatus(MonitorStatus status, string statusText)
    {
        _startItem.Enabled = status == MonitorStatus.Paused;
        _pauseItem.Enabled = status != MonitorStatus.Paused;
        var text = $"MousePilot - {statusText}";
        _notifyIcon.Text = text.Length <= 63 ? text : text[..63];
    }

    public ToolStripMenuItem? FindMenuItem(string text)
        => _menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(i => i.Text == text);

    public IReadOnlyList<string> MenuTexts
        => _menu.Items.OfType<ToolStripItem>()
            .Select(i => i is ToolStripSeparator ? "-" : i.Text ?? "")
            .ToList();

    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        using (var brand = new SolidBrush(Color.FromArgb(22, 163, 74)))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            g.FillEllipse(brand, 0, 0, 15, 15);        // 品牌綠圓（同 StatusRunningBrush #16A34A）
            g.FillEllipse(Brushes.White, 6, 3, 4, 7);  // 滑鼠滾輪意象
        }

        var hIcon = bmp.GetHicon();
        try
        {
            using var fromHandle = Icon.FromHandle(hIcon);
            return (Icon)fromHandle.Clone(); // Clone 擁有獨立資源，原 handle 可安全銷毀
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false; // 先隱藏避免系統匣殘影
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }
}
```

- [ ] **Step 5: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 全綠（98 + 5 = 103）。

- [ ] **Step 6: Commit**

```text
feat: TrayIconService - 系統匣圖示與選單（WinForms NotifyIcon）

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

`git add MousePilot.csproj tests/MousePilot.Tests/MousePilot.Tests.csproj Native/NativeMethods.cs Services/TrayIconService.cs tests/MousePilot.Tests/TrayIconServiceTests.cs`，`-F` 提交後驗證。

---

### Task 2: App 接線（Tray 生命週期、視窗顯隱、安全結束）

**Files:**
- Modify: `App.xaml`（ShutdownMode 改 OnExplicitShutdown）
- Modify: `App.xaml.cs`（整檔覆寫）
- Modify: `Views/MainWindow.xaml`（系統設定卡：新增「關閉視窗時最小化至系統匣」checkbox；移除「啟動後最小化至系統匣（Phase 4 生效）」的後綴）

**Interfaces:**
- Consumes: `TrayIconService`（Task 1 全部）、`MainViewModel`（StartCommand/PauseCommand/MoveOnceCommand/Status/StatusText/Settings/Dispose/SaveSettings、`INotifyPropertyChanged`）。

- [ ] **Step 1: App.xaml 修改**

`ShutdownMode="OnMainWindowClose"` 改為 `ShutdownMode="OnExplicitShutdown"`（關窗改為縮匣後，App 生命週期由 Tray 的「結束」控制）。

- [ ] **Step 2: App.xaml.cs 整檔覆寫**

```csharp
using System.ComponentModel;
using System.Windows;
using MousePilot.Services;
using MousePilot.ViewModels;
using MousePilot.Views;

namespace MousePilot;

public partial class App : Application
{
    private MainViewModel? _mainViewModel;
    private TrayIconService? _tray;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var vm = new MainViewModel(new SettingsService()); // 區域變數供 lambda 捕捉（避免 nullable 欄位的 CS8602 警告）
        _mainViewModel = vm;
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        window.Closing += OnMainWindowClosing;

        _tray = new TrayIconService();
        _tray.OpenRequested += ShowDashboard;
        _tray.StartRequested += () => vm.StartCommand.Execute(null);
        _tray.PauseRequested += () => vm.PauseCommand.Execute(null);
        _tray.MoveOnceRequested += () => vm.MoveOnceCommand.Execute(null);
        _tray.ExitRequested += ExitApplication;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        _tray.UpdateStatus(vm.Status, vm.StatusText);

        if (!vm.Settings.StartMinimized)
        {
            window.Show();
        }
        // StartMinimized=true（規格 §16 預設）：不顯示視窗，僅系統匣
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Status) or nameof(MainViewModel.StatusText)
            && _mainViewModel is not null)
        {
            _tray?.UpdateStatus(_mainViewModel.Status, _mainViewModel.StatusText);
        }
    }

    private void ShowDashboard()
    {
        if (MainWindow is null)
        {
            return;
        }

        MainWindow.Show();
        if (MainWindow.WindowState == WindowState.Minimized)
        {
            MainWindow.WindowState = WindowState.Normal;
        }

        MainWindow.Activate();
    }

    private void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting)
        {
            return;
        }

        if (_mainViewModel?.Settings.MinimizeToTrayOnClose == true)
        {
            e.Cancel = true;       // 關閉 → 縮到系統匣（規格 §13 預設）
            MainWindow?.Hide();
        }
        else
        {
            ExitApplication();     // 設定為真的關閉
        }
    }

    /// <summary>安全結束流程（規格 §30 現階段子集；hotkey/cursor/mutex 由 Phase 6/9/10 插入對應步驟）。</summary>
    private void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _mainViewModel?.Dispose();      // 1~3：取消進行中移動、停止輪詢 timer
        _tray?.Dispose();               // 6：系統匣圖示
        _mainViewModel?.SaveSettings(); // 7：保存設定
        Shutdown();                     // 9：關閉程式
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 保險路徑：非 Tray 結束（例外等）也要保存與釋放
        if (!_exiting)
        {
            _mainViewModel?.SaveSettings();
            _mainViewModel?.Dispose();
            _tray?.Dispose();
        }

        base.OnExit(e);
    }
}
```

- [ ] **Step 3: MainWindow.xaml 系統設定卡修改**

1. `啟動後最小化至系統匣（Phase 4 生效）` 改為 `啟動後最小化至系統匣`。
2. 在該 checkbox 之後新增：

```xml
                    <CheckBox Content="關閉視窗時最小化至系統匣"
                              IsChecked="{Binding Settings.MinimizeToTrayOnClose}" Margin="0,0,0,6"/>
```

- [ ] **Step 4: Build + 測試 + 啟動冒煙**

Run: `dotnet build -c Release`（0 error）、`dotnet test tests/MousePilot.Tests`（103 綠）。
啟動冒煙（無螢幕替代）：背景啟動 app、等 4 秒——**注意 StartMinimized 預設 true，不會有視窗**，確認程序存活即可；Stop-Process 收掉，如實記錄。（Tray icon 本身無法 headless 驗證，列使用者手動清單。）

- [ ] **Step 5: Commit**

```text
feat: 系統匣接線 - 關閉縮匣/啟動最小化/安全結束流程

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

`git add App.xaml App.xaml.cs Views/MainWindow.xaml`，`-F` 提交後驗證。

---

### Task 3: Phase 收尾

**Files:**
- Modify: `CHANGELOG.md`
- Modify: `docs/superpowers/plans/2026-08-20-mousepilot-master-plan.md`（Phase 4 → ✅ 完成、細部計畫文件欄填 `2026-08-21-phase4-system-tray.md`；Phase 4 風險列的「傾向 PInvoke」註記改為「已決策：WinForms NotifyIcon（無新 NuGet、自帶 Explorer 重啟重建）」）

- [ ] **Step 1: CHANGELOG [Unreleased] 的「### 新增」補上**

```markdown
- 系統匣（Phase 4）：tray icon 與右鍵選單（開啟/啟動/暫停/立即執行一次/游標佔位三項/設定/結束）、雙擊開 Dashboard、關閉視窗縮到系統匣（可設定）、啟動後最小化到系統匣（預設）、安全結束流程。
```

- [ ] **Step 2: Master Plan 更新（僅 Phase 4 列與 Phase 4 章節的決策註記）**

- [ ] **Step 3: 最終驗證**

```powershell
dotnet build -c Release
dotnet test tests/MousePilot.Tests
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

Expected: 全部成功；EXE 於 `bin\Release\net8.0-windows\win-x64\publish\`。

- [ ] **Step 4: Commit**

```text
docs: 更新 CHANGELOG 與進度總表 - Phase 4 完成

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

## Phase 4 完成定義

- [ ] build 0 error、測試全綠（預期 103）、publish 成功。
- [ ] 單元測試涵蓋：選單結構順序、游標三項停用、啟動/暫停可用性切換、五種事件轉發、tooltip 截斷。
- [ ] **使用者實機手動驗證（規格 §34 案例 25 + §13/§16）：**
  1. 啟動 EXE：預設不顯示視窗、系統匣出現綠色圓形 icon；tooltip 顯示狀態。
  2. 雙擊 icon → Dashboard 開啟；右鍵 → 九項選單、游標三項灰色。
  3. 選單「暫停」→ Dashboard 狀態變已暫停、選單「啟動」恢復（可用性同步切換）。
  4. 「立即執行一次」→ 滑鼠微移。
  5. 按視窗 X → 視窗消失、程式仍在系統匣；取消勾選「關閉視窗時最小化至系統匣」後按 X → 程式完全結束。
  6. 「結束」→ icon 消失、程序結束、設定已保存（重開驗證）。
  7. 工作管理員重啟 Windows 檔案總管 → tray icon 自動重建（案例 25）。
