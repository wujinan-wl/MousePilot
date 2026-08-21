# MousePilot Phase 5：Startup Registry 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 「Windows 開機時自動啟動」生效：checkbox 寫入/移除 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，啟動時與 Registry 實際狀態同步並自我修復移動後的 EXE 路徑，寫入失敗不 crash（Notice 提示 + checkbox 還原）。

**Architecture:** `StartupService`（Registry 讀寫，key 路徑/value 名稱/EXE 路徑皆可注入，方法 `virtual` 供測試偽造失敗；測試對 HKCU 下的臨時測試 key 做真實讀寫並清理）→ VM 包裝屬性 `RunAtStartup`（setter 內先寫 Registry、成功才更新 Settings，失敗顯示 Notice 並讓 checkbox 還原）→ XAML 綁定改指向 VM 屬性。EXE 路徑用 `Environment.ProcessPath`（**PublishSingleFile 下 `Assembly.Location` 為空字串，不可用**），寫入時以引號包覆。

**Tech Stack:** 既有（`Microsoft.Win32.Registry` 為 net8.0-windows 內建）。無新相依。

**Spec:** `docs/spec/mousepilot-spec.md`（§15、§21、§34 案例 23/24）；Master Plan Phase 5 章節。

## 計畫決策（供使用者知悉，可否決）

- **`runAtStartup` 預設維持 `false`**（Phase 2 ledger 遺留的對齊點）：規格 §18 的 JSON 範例值為 true，但那是範例非預設值規範；首次啟動就靜默寫入開機自啟過於侵入，與 §40「不干擾使用者」精神相悖。使用者勾選才寫入。
- **Registry 為此設定的真實來源**：啟動時讀 Registry 實際狀態回填 `Settings.RunAtStartup`（使用者可能在外部工具移除過）；若已註冊則冪等重寫目前 EXE 路徑（修復 Portable EXE 被移動的情境，Master Plan 風險項）。

## Global Constraints

- 只碰 `HKCU`，絕不碰 HKLM（規格 §15，不需 Administrator）。Run key：`Software\Microsoft\Windows\CurrentVersion\Run`，value 名稱 `MousePilot`，value 內容為引號包覆的 EXE 完整路徑。
- Registry 例外（SecurityException/UnauthorizedAccessException/IOException）一律吞入回傳 false，不得讓程式退出（規格 §21）；VM 層失敗 → Notice + checkbox 還原。
- EXE 路徑來源：`Environment.ProcessPath`；為 null/空時 Enable 回傳 false（保守）。
- 測試不得寫入真實 Run key——一律用 `Software\MousePilotTests\<guid>\Run` 臨時 key，測試結束 `DeleteSubKeyTree` 清理。
- **（review 修正補充）**測試檔內**每一個** `new MainViewModel(` 建構點都必須明確注入 startupService（無 registry 行為的 `NoOpStartupService` 或測試 key 的實例）——VM 第四參數預設會建立指向真實 Run key 的 StartupService，且建構子同步在非 pristine 機器上會以 testhost 路徑改寫真實開機項。後續 Phase 新增 VM 測試時同樣適用。
- TDD：先測試後實作；目前基準 103 綠。
- Commit 一律 `git commit -F <暫存檔>`（暫存檔放 `$env:TEMP`，禁 here-string），繁中+前綴+`Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`，commit 後 `git log -1 --format=%B` 驗證。

---

### Task 1: StartupService（TDD）

**Files:**
- Create: `Services/StartupService.cs`
- Test: `tests/MousePilot.Tests/StartupServiceTests.cs`

**Interfaces:**
- Produces（Task 2 依賴，逐字）:
  - `class StartupService`（不 sealed）：建構子 `StartupService(string? exePath = null, string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run", string valueName = "MousePilot")`（exePath null → `Environment.ProcessPath`）；`public virtual bool Enable()`、`public virtual bool Disable()`、`public virtual bool? IsEnabled()`（null=讀取失敗）。

- [ ] **Step 1: 寫失敗測試**

`tests/MousePilot.Tests/StartupServiceTests.cs`：

```csharp
using Microsoft.Win32;
using MousePilot.Services;

namespace MousePilot.Tests;

public sealed class StartupServiceTests : IDisposable
{
    private readonly string _testRoot = @"Software\MousePilotTests\" + Guid.NewGuid().ToString("N");

    private string RunKeyPath => _testRoot + @"\Run";

    public void Dispose()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(_testRoot, throwOnMissingSubKey: false);
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private StartupService Create(string exePath = @"C:\Apps\MousePilot.exe")
        => new(exePath, RunKeyPath);

    [Fact]
    public void Enable寫入引號包覆的EXE路徑()
    {
        var service = Create(@"C:\My Apps\MousePilot.exe");
        Assert.True(service.Enable());

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        Assert.Equal("\"C:\\My Apps\\MousePilot.exe\"", key!.GetValue("MousePilot"));
    }

    [Fact]
    public void Enable後IsEnabled為true()
    {
        var service = Create();
        service.Enable();
        Assert.True(service.IsEnabled());
    }

    [Fact]
    public void 未註冊時IsEnabled為false()
    {
        Assert.False(Create().IsEnabled());
    }

    [Fact]
    public void Disable移除值()
    {
        var service = Create();
        service.Enable();
        Assert.True(service.Disable());
        Assert.False(service.IsEnabled());
    }

    [Fact]
    public void Disable於值不存在時仍回傳成功()
    {
        Assert.True(Create().Disable());
    }

    [Fact]
    public void EXE路徑為空時Enable失敗不擲例外()
    {
        var service = new StartupService("", RunKeyPath);
        Assert.False(service.Enable());
        Assert.False(service.IsEnabled());
    }

    [Fact]
    public void 重新Enable以新路徑更新值()
    {
        Create(@"C:\Old\MousePilot.exe").Enable();
        Create(@"D:\New\MousePilot.exe").Enable();  // Portable EXE 被移動後的修復

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        Assert.Equal("\"D:\\New\\MousePilot.exe\"", key!.GetValue("MousePilot"));
    }
}
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 編譯失敗（`StartupService` 不存在）。

- [ ] **Step 3: 實作 Services/StartupService.cs**

```csharp
using System.IO;
using Microsoft.Win32;

namespace MousePilot.Services;

/// <summary>
/// 開機自動啟動（規格 §15）：只讀寫 HKCU 的 Run key，絕不碰 HKLM（不需 Administrator）。
/// 方法為 virtual 供測試偽造失敗；key 路徑可注入供測試用臨時 key。
/// EXE 路徑用 Environment.ProcessPath——PublishSingleFile 下 Assembly.Location 為空，不可用。
/// </summary>
public class StartupService
{
    private readonly string? _exePath;
    private readonly string _runKeyPath;
    private readonly string _valueName;

    public StartupService(
        string? exePath = null,
        string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run",
        string valueName = "MousePilot")
    {
        _exePath = exePath ?? Environment.ProcessPath;
        _runKeyPath = runKeyPath;
        _valueName = valueName;
    }

    /// <summary>寫入（或以目前 EXE 路徑更新）開機自啟。失敗回傳 false，不擲例外（規格 §21）。</summary>
    public virtual bool Enable()
    {
        if (string.IsNullOrEmpty(_exePath))
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(_runKeyPath);
            if (key is null)
            {
                return false;
            }

            key.SetValue(_valueName, $"\"{_exePath}\"");
            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>移除開機自啟。值不存在視為成功；失敗回傳 false，不擲例外。</summary>
    public virtual bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true);
            key?.DeleteValue(_valueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>目前是否已註冊開機自啟；null = 讀取失敗（呼叫端不應據以改動任何狀態）。</summary>
    public virtual bool? IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath);
            return key?.GetValue(_valueName) is string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 全綠（103 + 7 = 110）。

- [ ] **Step 5: Commit**

```text
feat: StartupService - HKCU Run key 開機自啟讀寫

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

`git add Services/StartupService.cs tests/MousePilot.Tests/StartupServiceTests.cs`，`-F` 提交後驗證。

---

### Task 2: VM 整合與 XAML（TDD）

**Files:**
- Modify: `ViewModels/MainViewModel.cs`
- Modify: `Views/MainWindow.xaml`（開機自啟 checkbox 綁定與文案）
- Test: `tests/MousePilot.Tests/MainViewModelTests.cs`（新增測試）

**Interfaces:**
- Consumes: `StartupService.Enable/Disable/IsEnabled`（Task 1）。
- Produces: `MainViewModel` 建構子第四參數 `StartupService? startupService = null`（null → `new StartupService()`）；`public bool RunAtStartup`（包裝屬性：setter 先寫 Registry、成功才更新 `Settings.RunAtStartup`、失敗設 Notice 並觸發 PropertyChanged 讓 checkbox 還原）；建構時同步：`IsEnabled()` 非 null 時回填 `Settings.RunAtStartup`，且已註冊時冪等重寫目前 EXE 路徑。

- [ ] **Step 1: 寫失敗測試**

`tests/MousePilot.Tests/MainViewModelTests.cs` 新增（class 內末尾）：

```csharp
    private sealed class FailingStartupService : StartupService
    {
        public FailingStartupService() : base(@"C:\Apps\MousePilot.exe", @"Software\MousePilotTests\unused\Run") { }

        public override bool Enable() => false;

        public override bool Disable() => false;

        public override bool? IsEnabled() => null;
    }

    private string _startupTestRoot = "";

    private StartupService CreateStartupService(string exePath = @"C:\Apps\MousePilot.exe")
    {
        if (_startupTestRoot.Length == 0)
        {
            _startupTestRoot = @"Software\MousePilotTests\" + Guid.NewGuid().ToString("N");
        }

        return new StartupService(exePath, _startupTestRoot + @"\Run");
    }

    private void CleanupStartupKey()
    {
        if (_startupTestRoot.Length > 0)
        {
            try
            {
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(_startupTestRoot, throwOnMissingSubKey: false);
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public void 勾選開機自啟寫入Registry並更新設定()
    {
        try
        {
            var startup = CreateStartupService();
            var vm = CreateVmWithStartup(startup);

            vm.RunAtStartup = true;

            Assert.True(vm.Settings.RunAtStartup);
            Assert.True(startup.IsEnabled());

            vm.RunAtStartup = false;

            Assert.False(vm.Settings.RunAtStartup);
            Assert.False(startup.IsEnabled());
        }
        finally
        {
            CleanupStartupKey();
        }
    }

    [Fact]
    public void Registry失敗時顯示提示且值不變()
    {
        var vm = CreateVmWithStartup(new FailingStartupService());

        vm.RunAtStartup = true;

        Assert.False(vm.Settings.RunAtStartup);   // 失敗 → 不更新
        Assert.False(vm.RunAtStartup);            // checkbox 讀回仍為 false
        Assert.Contains("開機自動啟動", vm.Notice);
    }

    [Fact]
    public void 啟動時同步Registry實際狀態並修復路徑()
    {
        try
        {
            CreateStartupService(@"C:\Old\MousePilot.exe").Enable(); // 外部已註冊、路徑過期
            var current = CreateStartupService(@"D:\New\MousePilot.exe");

            var vm = CreateVmWithStartup(current);                    // settings 檔內 runAtStartup 未設（false）

            Assert.True(vm.Settings.RunAtStartup);                    // 回填實際狀態
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(_startupTestRoot + @"\Run");
            Assert.Equal("\"D:\\New\\MousePilot.exe\"", key!.GetValue("MousePilot")); // 路徑已修復
        }
        finally
        {
            CleanupStartupKey();
        }
    }

    private MainViewModel CreateVmWithStartup(StartupService startup)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{\"autoStartMonitoring\": false, \"idleStartSeconds\": 5}");
        var clock = new TestClock();
        return new MainViewModel(new SettingsService(SettingsPath),
            s => new IdleDetectionService(s, () => clock.Now, () => clock.LastInput, () => (0, 0)),
            (s, idle) => new MouseMovementService(
                s, idle,
                cursorProvider: () => (0, 0),
                boundsProvider: () => new ScreenBounds(0, 0, 1920, 1080),
                sendMove: (_, _) => true,
                correctPosition: (_, _) => true,
                lastInputProvider: () => 0u,
                delay: (_, _) => Task.CompletedTask,
                randomIndexProvider: () => 0),
            startup);
    }
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 編譯失敗（VM 無第四參數/`RunAtStartup` 屬性）。

- [ ] **Step 3: 實作（`ViewModels/MainViewModel.cs` 修改）**

1. 欄位與建構子第四參數：

```csharp
    private readonly StartupService _startupService;
```

```csharp
    public MainViewModel(
        SettingsService settingsService,
        Func<AppSettings, IdleDetectionService>? idleServiceFactory = null,
        Func<AppSettings, IdleDetectionService, MouseMovementService>? movementServiceFactory = null,
        StartupService? startupService = null)
```

2. 建構子內（`_movementService = ...` 之後、`IdleService.Ticked += ...` 之前）加：

```csharp
        _startupService = startupService ?? new StartupService();
        // Registry 為此設定的真實來源：回填實際狀態；已註冊則冪等重寫目前 EXE 路徑（修復移動後失效）
        if (_startupService.IsEnabled() is { } registered)
        {
            Settings.RunAtStartup = registered;
            if (registered)
            {
                _startupService.Enable();
            }
        }
```

3. 新增包裝屬性（放在 `SaveSettings()` 之前）：

```csharp
    /// <summary>開機自啟（規格 §15）：先寫 Registry、成功才更新設定；失敗顯示提示並讓 checkbox 還原。</summary>
    public bool RunAtStartup
    {
        get => Settings.RunAtStartup;
        set
        {
            if (value == Settings.RunAtStartup)
            {
                return;
            }

            var ok = value ? _startupService.Enable() : _startupService.Disable();
            if (ok)
            {
                Settings.RunAtStartup = value;
            }
            else
            {
                Notice = value
                    ? "無法寫入開機自動啟動設定（Registry 存取被拒）。"
                    : "無法移除開機自動啟動設定（Registry 存取被拒）。";
            }

            OnPropertyChanged(); // 失敗時 getter 仍回舊值 → checkbox 還原
        }
    }
```

- [ ] **Step 4: XAML 修改（`Views/MainWindow.xaml` 系統設定卡）**

`<CheckBox Content="Windows 開機時自動啟動（Phase 5 生效）" IsChecked="{Binding Settings.RunAtStartup}" ...` 改為：

```xml
                    <CheckBox Content="Windows 開機時自動啟動"
                              IsChecked="{Binding RunAtStartup}" Margin="0,0,0,6"/>
```

- [ ] **Step 5: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`（103 + 7 + 3 = 113 全綠）、`dotnet build -c Release`（0 error）。

- [ ] **Step 6: Commit**

```text
feat: 開機自動啟動生效 - VM 包裝屬性與啟動時同步修復

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

`git add ViewModels/MainViewModel.cs Views/MainWindow.xaml tests/MousePilot.Tests/MainViewModelTests.cs`，`-F` 提交後驗證。

---

### Task 3: Phase 收尾

**Files:**
- Modify: `CHANGELOG.md`
- Modify: `docs/superpowers/plans/2026-08-20-mousepilot-master-plan.md`（Phase 5 → ✅ 完成、細部計畫文件欄填 `2026-08-21-phase5-startup-registry.md`）

- [ ] **Step 1: CHANGELOG [Unreleased] 的「### 新增」補上**

```markdown
- 開機自動啟動（Phase 5）：checkbox 寫入/移除 HKCU Run key（引號包覆 EXE 路徑）、啟動時與 Registry 實際狀態同步並自我修復移動後的路徑、寫入失敗不 crash（提示 + checkbox 還原）。
```

- [ ] **Step 2: Master Plan 更新（僅 Phase 5 列與細部計畫文件欄）**

- [ ] **Step 3: 最終驗證**

```powershell
dotnet build -c Release
dotnet test tests/MousePilot.Tests
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

- [ ] **Step 4: Commit**

```text
docs: 更新 CHANGELOG 與進度總表 - Phase 5 完成

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

## Phase 5 完成定義

- [ ] build 0 error、測試全綠（預期 113）、publish 成功。
- [ ] 單元測試涵蓋：引號路徑寫入、移除、不存在移除成功、空路徑保守失敗、路徑更新修復、VM 勾選/取消流程、失敗還原提示、啟動同步+修復。
- [ ] **使用者實機手動驗證（規格 §34 案例 23/24）：**
  1. 勾選「Windows 開機時自動啟動」→ PowerShell 執行 `Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name MousePilot` 應顯示引號包覆的 EXE 路徑（案例 23）。
  2. 取消勾選 → 同指令應報「找不到屬性」（案例 24）。
  3. 把 publish 的 EXE 複製到另一個資料夾、勾選狀態下執行它 → Run key 的路徑自動更新為新位置。
  4. （可選）勾選後重新開機 → MousePilot 自動啟動且直接進系統匣。
