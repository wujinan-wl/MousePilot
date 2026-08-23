# MousePilot Phase 9：Global Cursor 套用/恢復 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 全域游標套用（`SetSystemCursor`）與安全恢復（`SystemParametersInfo(SPI_SETCURSORS)`）；恢復掛上**所有**退出路徑（按鈕、F10、Tray、正常關閉、未處理例外、SessionEnding、crash 後下次啟動補救、`--restore-cursor` 參數）；「確定」時落地 confirmed .cur 關閉 WYSIWYG 缺口；Tray 三個游標選單項與主視窗 套用/恢復 按鈕啟用。

**Architecture:** 新 `CursorService`（PInvoke 全注入可測；marker 檔標記「已套用未恢復」狀態供 crash 補救）；套用一律 `LoadCursorFromFile(Settings.ConfirmedCursorFile)`——editor「確定」時把 `CurrentCurBytes` 落地為 `%AppData%\MousePilot\confirmed-cursor.cur`（.cur/.ani 來源記原檔路徑），三種來源同一條套用路徑、免重跑影像管線（Phase 8 移交 (g) 首選方案）。**實作順序固定：恢復路徑先寫先測，替換後寫**（Master Plan 風險要求）。

**Tech Stack:** 既有 + user32：`LoadCursorFromFile` / `SetSystemCursor` / `DestroyCursor` / `SystemParametersInfo(SPI_SETCURSORS)`（Spike B 已驗證）。

**Spec:** `docs/spec/mousepilot-spec.md`（§11、§12 游標卡、§13 Tray、§17 F10、§21、§30 步驟5、§34 案例 20~22）；Master Plan Phase 9 章節（移交 a/b/c~f/g~j）。

## 計畫決策（供使用者知悉，可否決）

1. **只替換 `OCR_NORMAL`（標準箭頭）**：文字游標/等待游標等其餘角色不動——全部角色換同一圖示會讓 I-beam 等場景怪異；規格只要求「更換滑鼠游標」。
2. **confirmed-cursor.cur 落地 `%AppData%\MousePilot\` 根目錄**（非 `Cursors\` 子目錄）——避免被 gallery 列舉/移除。
3. **啟動時 `CustomCursorEnabled=true` → 自動重新套用**（延續使用者上次狀態）；crash 補救（marker → SPI_SETCURSORS）先行執行。
4. **「停用自訂游標」與「恢復 Windows 游標」等價**（皆 Restore + `CustomCursorEnabled=false`）——若恢復不清 enabled，重啟會自動套回，違反最小驚訝。
5. **`--restore-cursor` 命令列參數**：只執行 SPI_SETCURSORS 後結束（緊急補救，Spike B `--restore-only` 語意）。

## Global Constraints

- **恢復優先**（規格 §11/設計優先序 2）：`SetSystemCursor` 會**接管並銷毀**傳入 handle（Spike B）——handle 一律新載入、失敗路徑必須 `DestroyCursor`；恢復一律 `SPI_SETCURSORS` 重載使用者 Cursor Scheme，**永不**保存/寫回個別游標（不可能破壞使用者原始設定）。
- marker 檔 `%AppData%\MousePilot\cursor-applied.marker`：Apply 成功寫入、Restore 成功刪除；Restore 失敗**保留**（下次啟動再補救）。
- 所有 PInvoke 集中 `Native/NativeMethods.cs`；`CursorService` 的 PInvoke 全部經建構子注入（測試零真實 Win32 呼叫）。
- 測試絕不觸真實 Registry / `%AppData%`：marker path 注入 temp 目錄；**MainViewModel 測試必須注入 `FakeCursorService` 與測試安全的 confirmedWriter**（新第 9/10 參數 default null → 建構期零副作用，既有建構點不需改）。
- 移交落實：(d) 退化已在 Phase 8 擋於確定前；(f) .ani 走 `LoadCursorFromFile` 原檔；(h) `AppSettings.Clamp()` 補 hotspot 夾制與 preset/file 互斥（preset 優先）；(i) CursorFile 來源套用走檔內值（ConfirmedCursorFile=原檔，size/hotspot 殘值不參與）。
- 未處理例外 hook 本階段**只做恢復游標**（不吞例外、不 Handled）——完整 handler 是 Phase 11。
- TDD；綠了才 commit；commit 用 `$env:TEMP` 暫存檔 + `git commit -F`（禁 here-string），繁中+前綴+`Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`，commit 後 `git log -1 --format=%B` 驗證；禁止對 docs/ 或非任務檔案 git 還原。現況基準 221 綠。

---

### Task 1: AppSettings 強化 + NativeMethods + CursorService（TDD；恢復先寫先測）

**Files:**
- Modify: `Models/AppSettings.cs`（`ConfirmedCursorFile` 欄位 + Clamp 強化）
- Modify: `Native/NativeMethods.cs`（游標 PInvoke）
- Create: `Services/CursorService.cs`
- Test: `tests/MousePilot.Tests/AppSettingsTests.cs`（+3）、`tests/MousePilot.Tests/CursorServiceTests.cs`（新，8）

**Interfaces:**
- Produces（後續 task 依賴，逐字）:
  - `AppSettings.ConfirmedCursorFile : string`（default `""`；camelCase JSON 自動）。
  - `NativeMethods`：`internal static extern IntPtr LoadCursorFromFile(string)`、`internal static extern bool SetSystemCursor(IntPtr, uint)`、`internal static extern bool DestroyCursor(IntPtr)`、`internal const uint OCR_NORMAL = 32512;`、`internal static bool ReloadCursorScheme()`。
  - `class CursorService : IDisposable`：建構子 `(Func<string, IntPtr>? loadCursorFromFile = null, Func<IntPtr, bool>? setSystemCursor = null, Func<bool>? reloadScheme = null, Action<IntPtr>? destroyCursor = null, string? markerPath = null)`；`public virtual bool IsApplied { get; protected set; }`；`public bool HasPendingRestore`；`public virtual bool Apply(string curFilePath)`；`public virtual bool Restore()`；`Dispose()`（IsApplied → Restore）。

- [ ] **Step 1: 寫失敗測試**

`tests/MousePilot.Tests/AppSettingsTests.cs` 新增：

```csharp
    [Fact]
    public void Clamp夾制Hotspot於尺寸範圍()
    {
        var s = new AppSettings { CursorSize = 32, CursorHotspotX = 999, CursorHotspotY = -5 };
        s.Clamp();
        Assert.Equal(31, s.CursorHotspotX);
        Assert.Equal(0, s.CursorHotspotY);
    }

    [Fact]
    public void Clamp互斥時保留Preset()
    {
        var s = new AppSettings { CursorPreset = "Arrow", CursorFile = @"C:\x.png" };
        s.Clamp();
        Assert.Equal("Arrow", s.CursorPreset);
        Assert.Equal("", s.CursorFile);
    }

    [Fact]
    public void ConfirmedCursorFile預設空並於null時正規化()
    {
        Assert.Equal("", new AppSettings().ConfirmedCursorFile);
        var s = new AppSettings { ConfirmedCursorFile = null! };
        s.Clamp();
        Assert.Equal("", s.ConfirmedCursorFile);
    }
```

`tests/MousePilot.Tests/CursorServiceTests.cs`（新檔）：

```csharp
using MousePilot.Services;

namespace MousePilot.Tests;

public sealed class CursorServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "MousePilotCursorSvcTests", Guid.NewGuid().ToString("N"));

    public CursorServiceTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(CurPath, new byte[] { 0, 0, 2, 0 });
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string MarkerPath => Path.Combine(_dir, "cursor-applied.marker");

    private string CurPath => Path.Combine(_dir, "test.cur");

    private CursorService Create(
        Func<string, IntPtr>? load = null,
        Func<IntPtr, bool>? set = null,
        Func<bool>? reload = null,
        Action<IntPtr>? destroy = null)
        => new(load ?? (_ => new IntPtr(42)), set ?? (_ => true), reload ?? (() => true), destroy ?? (_ => { }), MarkerPath);

    // === 恢復路徑（先寫先測——Master Plan 風險要求） ===

    [Fact]
    public void 恢復成功刪除marker並清除狀態()
    {
        File.WriteAllText(MarkerPath, "");
        var svc = Create();

        Assert.True(svc.Restore());
        Assert.False(File.Exists(MarkerPath));
        Assert.False(svc.IsApplied);
    }

    [Fact]
    public void 恢復失敗保留marker供下次補救()
    {
        File.WriteAllText(MarkerPath, "");
        var svc = Create(reload: () => false);

        Assert.False(svc.Restore());
        Assert.True(File.Exists(MarkerPath));
        Assert.True(svc.HasPendingRestore);
    }

    // === 套用路徑 ===

    [Fact]
    public void 套用成功寫入marker()
    {
        var svc = Create();

        Assert.True(svc.Apply(CurPath));
        Assert.True(svc.IsApplied);
        Assert.True(svc.HasPendingRestore);
    }

    [Fact]
    public void 檔案不存在時套用失敗()
    {
        var svc = Create();

        Assert.False(svc.Apply(Path.Combine(_dir, "nope.cur")));
        Assert.False(svc.IsApplied);
        Assert.False(File.Exists(MarkerPath));
    }

    [Fact]
    public void 載入失敗時套用失敗不寫marker()
    {
        var svc = Create(load: _ => IntPtr.Zero);

        Assert.False(svc.Apply(CurPath));
        Assert.False(File.Exists(MarkerPath));
    }

    [Fact]
    public void 替換失敗時銷毀handle不寫marker()
    {
        var destroyed = new List<IntPtr>();
        var svc = Create(set: _ => false, destroy: destroyed.Add);

        Assert.False(svc.Apply(CurPath));
        Assert.Equal(new[] { new IntPtr(42) }, destroyed);
        Assert.False(File.Exists(MarkerPath));
    }

    // === Dispose 保險 ===

    [Fact]
    public void Dispose時已套用則恢復()
    {
        var reloaded = 0;
        var svc = Create(reload: () => { reloaded++; return true; });
        svc.Apply(CurPath);

        svc.Dispose();

        Assert.Equal(1, reloaded);
        Assert.False(svc.IsApplied);
    }

    [Fact]
    public void Dispose時未套用不動作()
    {
        var reloaded = 0;
        var svc = Create(reload: () => { reloaded++; return true; });

        svc.Dispose();

        Assert.Equal(0, reloaded);
    }
}
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 編譯失敗（ConfirmedCursorFile/CursorService 不存在）。

- [ ] **Step 3: 實作**

`Models/AppSettings.cs`：屬性區加

```csharp
    /// <summary>「確定」時落地的實際套用檔（processed .cur 或原 .cur/.ani 路徑）；Phase 9 套用一律讀此欄。</summary>
    public string ConfirmedCursorFile { get; set; } = "";
```

`Clamp()` 內：null 正規化區加 `ConfirmedCursorFile ??= "";`；並於 `CursorSize` 夾制**之後**加

```csharp
        // 移交 (h)：手改 settings.json 防護——hotspot 夾制於尺寸範圍；preset/file 互斥（preset 優先，與 RefreshCursorFileText 一致）
        CursorHotspotX = Math.Clamp(CursorHotspotX, 0, CursorSize - 1);
        CursorHotspotY = Math.Clamp(CursorHotspotY, 0, CursorSize - 1);
        if (CursorPreset.Length > 0 && CursorFile.Length > 0)
        {
            CursorFile = "";
        }
```

`Native/NativeMethods.cs` 加（既有常數/方法區，維持檔內排列風格）：

```csharp
    internal const uint OCR_NORMAL = 32512;

    private const uint SPI_SETCURSORS = 0x0057;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr LoadCursorFromFile(string lpFileName);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetSystemCursor(IntPtr hcur, uint id);

    [DllImport("user32.dll")]
    internal static extern bool DestroyCursor(IntPtr hCursor);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    /// <summary>重載使用者的 Windows Cursor Scheme（恢復所有系統游標；Spike B 驗證）。</summary>
    internal static bool ReloadCursorScheme() => SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0);
```

`Services/CursorService.cs`：

```csharp
using System.IO;
using MousePilot.Native;

namespace MousePilot.Services;

/// <summary>
/// 全域游標套用/恢復（規格 §11、Spike B）。
/// 恢復優先：SPI_SETCURSORS 重載使用者 Cursor Scheme，永不保存/寫回個別游標——不可能破壞使用者原始設定。
/// marker 檔＝「已套用未恢復」：Apply 成功寫入、Restore 成功刪除；啟動時若存在代表上次未正常恢復（crash），先補救。
/// </summary>
public class CursorService : IDisposable
{
    private readonly Func<string, IntPtr> _loadCursorFromFile;
    private readonly Func<IntPtr, bool> _setSystemCursor;
    private readonly Func<bool> _reloadScheme;
    private readonly Action<IntPtr> _destroyCursor;
    private readonly string _markerPath;

    public CursorService(
        Func<string, IntPtr>? loadCursorFromFile = null,
        Func<IntPtr, bool>? setSystemCursor = null,
        Func<bool>? reloadScheme = null,
        Action<IntPtr>? destroyCursor = null,
        string? markerPath = null)
    {
        _loadCursorFromFile = loadCursorFromFile ?? NativeMethods.LoadCursorFromFile;
        _setSystemCursor = setSystemCursor ?? (h => NativeMethods.SetSystemCursor(h, NativeMethods.OCR_NORMAL));
        _reloadScheme = reloadScheme ?? NativeMethods.ReloadCursorScheme;
        _destroyCursor = destroyCursor ?? (h => NativeMethods.DestroyCursor(h));
        _markerPath = markerPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MousePilot", "cursor-applied.marker");
    }

    public virtual bool IsApplied { get; protected set; }

    /// <summary>marker 存在＝上次套用後未恢復（crash/強制結束）。</summary>
    public bool HasPendingRestore => File.Exists(_markerPath);

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

        // SetSystemCursor 接管並銷毀傳入 handle（Spike B）；失敗時必須自行銷毀避免洩漏
        if (!_setSystemCursor(handle))
        {
            _destroyCursor(handle);
            return false;
        }

        WriteMarker();
        IsApplied = true;
        return true;
    }

    public virtual bool Restore()
    {
        if (!_reloadScheme())
        {
            return false; // 失敗保留 marker，下次啟動補救
        }

        DeleteMarker();
        IsApplied = false;
        return true;
    }

    public void Dispose()
    {
        if (IsApplied)
        {
            Restore();
        }
    }

    private void WriteMarker()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
            File.WriteAllText(_markerPath, "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // marker 寫入失敗不阻止套用（僅失去 crash 補救能力）
        }
    }

    private void DeleteMarker()
    {
        try
        {
            if (File.Exists(_markerPath))
            {
                File.Delete(_markerPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 全綠（221 + 3 + 8 = 232）。`dotnet build -c Release` 0 警告。

- [ ] **Step 5: Commit**

```text
feat: CursorService 與設定防護 - 恢復優先的全域游標核心

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

### Task 2: Editor「確定」落地 confirmed 游標檔 + 套用按鈕（TDD）

**Files:**
- Modify: `ViewModels/CursorEditorViewModel.cs`
- Modify: `Views/CursorEditorWindow.xaml`（套用按鈕啟用）
- Test: `tests/MousePilot.Tests/CursorEditorViewModelTests.cs`（+4，並調整 Create helper）

**Interfaces:**
- Consumes: `AppSettings.ConfirmedCursorFile`（Task 1）。
- Produces: `CursorEditorViewModel` 建構子第 5 參數 `Func<byte[], string?>? confirmedWriter = null`（default 寫 `%AppData%\MousePilot\confirmed-cursor.cur`，失敗回 null）；`public bool ApplyRequested { get; private set; }`；`ConfirmAndApplyCommand`（CanExecute 同 Confirm）；`Confirm` 失敗（寫檔 null）→ Warning、不設 Confirmed、不觸發 CloseRequested。

- [ ] **Step 1: 寫失敗測試**

`CursorEditorViewModelTests.cs`：`Create` helper 加第 4 個可選參數並傳入 VM（**既有測試不需改**——default fake writer 回傳固定路徑）：

```csharp
    private CursorEditorViewModel Create(
        AppSettings? settings = null,
        Func<string, Bitmap?>? imageLoader = null,
        IReadOnlyList<string>? storedFiles = null,
        Func<byte[], string?>? confirmedWriter = null)
        => new(
            settings ?? new AppSettings(),
            imageLoader ?? (_ => Track(MakeOpaque(20, 10))),
            () => storedFiles ?? Array.Empty<string>(),
            _ => null,
            confirmedWriter ?? (_ => @"C:\confirmed\confirmed-cursor.cur")); // 測試安全 fake：不落地
```

新增測試：

```csharp
    [Fact]
    public void 確認時落地confirmed游標檔並記路徑()
    {
        byte[]? written = null;
        var settings = new AppSettings();
        var vm = Create(settings, confirmedWriter: b => { written = b; return @"C:\appdata\confirmed-cursor.cur"; });
        vm.SelectedSource = vm.Sources.First(s => s.Source.Id == "preset:Heart");

        vm.ConfirmCommand.Execute(null);

        Assert.Equal(vm.CurrentCurBytes, written);
        Assert.Equal(@"C:\appdata\confirmed-cursor.cur", settings.ConfirmedCursorFile);
        Assert.True(vm.Confirmed);
    }

    [Fact]
    public void 確認cur檔來源記原路徑不寫檔()
    {
        using var bmp = MakeOpaque(8, 8);
        var dir = Path.Combine(Path.GetTempPath(), "MousePilotEditorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var curPath = Path.Combine(dir, "x.cur");
        File.WriteAllBytes(curPath, CurFileFormat.Write(bmp, 2, 3));
        try
        {
            var called = false;
            var settings = new AppSettings();
            var vm = Create(settings, storedFiles: new[] { curPath }, confirmedWriter: _ => { called = true; return "x"; });
            vm.SelectedSource = vm.Sources.First(s => s.Source.Kind == CursorSourceKind.CursorFile);

            vm.ConfirmCommand.Execute(null);

            Assert.False(called); // .cur/.ani 原檔直接套用（.ani 動畫靠 LoadCursorFromFile 原生支援）
            Assert.Equal(curPath, settings.ConfirmedCursorFile);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void 寫檔失敗時不確認不關閉()
    {
        var settings = new AppSettings();
        var vm = Create(settings, confirmedWriter: _ => null);
        vm.SelectedSource = vm.Sources.First(s => s.Source.Id == "preset:Star");
        var closed = false;
        vm.CloseRequested += () => closed = true;

        vm.ConfirmCommand.Execute(null);

        Assert.False(vm.Confirmed);
        Assert.False(closed);
        Assert.Contains("寫入失敗", vm.Warning);
        Assert.Equal("", settings.ConfirmedCursorFile);
    }

    [Fact]
    public void 套用命令確認並標記套用請求()
    {
        var vm = Create();
        vm.SelectedSource = vm.Sources.First(s => s.Source.Id == "preset:Dot");

        vm.ConfirmAndApplyCommand.Execute(null);

        Assert.True(vm.Confirmed);
        Assert.True(vm.ApplyRequested);
    }
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests` → 編譯失敗。

- [ ] **Step 3: 實作（`ViewModels/CursorEditorViewModel.cs`）**

1. 欄位 `private readonly Func<byte[], string?> _confirmedWriter;`；建構子第 5 參數 `Func<byte[], string?>? confirmedWriter = null`，`_confirmedWriter = confirmedWriter ?? WriteConfirmedCur;`。
2. `Confirm()` 在互斥寫入區塊**之前**插入落地邏輯（整個方法改為）：

```csharp
    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        var source = SelectedSource!.Source;
        if (source.Kind == CursorSourceKind.CursorFile)
        {
            // 原檔直接套用（移交 (f)/(i)：.ani 由 LoadCursorFromFile 原生支援；size/hotspot 為顯示殘值不參與）
            _settings.ConfirmedCursorFile = source.FilePath!;
        }
        else
        {
            var written = _confirmedWriter(CurrentCurBytes!);
            if (written is null)
            {
                Warning = "游標檔寫入失敗，無法確認選擇。";
                return; // 不設 Confirmed、不關閉（規格 §21）
            }

            _settings.ConfirmedCursorFile = written;
        }

        if (source.Kind == CursorSourceKind.Preset)
        {
            _settings.CursorPreset = source.Id["preset:".Length..];
            _settings.CursorFile = ""; // 互斥：二擇一
        }
        else
        {
            _settings.CursorFile = source.FilePath!;
            _settings.CursorPreset = "";
        }

        _settings.CursorSize = SelectedSize;
        _settings.CursorHotspotX = HotspotX;
        _settings.CursorHotspotY = HotspotY;
        Confirmed = true;
        CloseRequested?.Invoke();
    }
```

3. 新增：

```csharp
    public bool ApplyRequested { get; private set; }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void ConfirmAndApply()
    {
        ApplyRequested = true;
        Confirm();
        if (!Confirmed)
        {
            ApplyRequested = false; // 確認失敗（寫檔失敗）→ 撤回套用請求
        }
    }

    /// <summary>production 落地路徑：%AppData%\MousePilot\confirmed-cursor.cur（根目錄，避免被 gallery 列舉）。</summary>
    private static string? WriteConfirmedCur(byte[] bytes)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MousePilot");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "confirmed-cursor.cur");
            File.WriteAllBytes(path, bytes);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
```

4. `Views/CursorEditorWindow.xaml` 套用按鈕改為：

```xml
                    <Button Content="套用" Command="{Binding ConfirmAndApplyCommand}" Padding="16,6"/>
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`（232 + 4 = 236 全綠）→ `dotnet build -c Release` 0 警告。

- [ ] **Step 5: Commit**

```text
feat: 編輯器確定落地 confirmed 游標檔與套用命令 - 關閉 WYSIWYG 缺口

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

### Task 3: MainViewModel 套用/恢復 + F10 + Tray 三項 + 主視窗卡（TDD）

**Files:**
- Modify: `ViewModels/MainViewModel.cs`、`Services/TrayIconService.cs`、`Views/MainWindow.xaml`、`Views/MainWindow.xaml.cs`（launcher 補 confirmedWriter？——不需，見下）
- Test: `tests/MousePilot.Tests/MainViewModelTests.cs`（+5）、`tests/MousePilot.Tests/TrayIconServiceTests.cs`（+1）

**Interfaces:**
- Consumes: `CursorService`（Task 1）、`CursorEditorViewModel.ApplyRequested` 與第 5 參數（Task 2）。
- Produces: `MainViewModel` 建構子第 9/10 參數 `CursorService? cursorService = null, Func<byte[], string?>? confirmedCurWriter = null`（皆 default null → 建構期零副作用；cursorService null → `new CursorService()`，confirmedCurWriter null → editor VM 用 production default）；`ApplyCursorCommand`（CanExecute：`Settings.ConfirmedCursorFile.Length > 0`）、`RestoreCursorCommand`；`CursorStatusText` 更新為「已套用自訂游標」/「Windows 預設」；`EditCursor` 建立 editor VM 時傳 `confirmedWriter: _confirmedCurWriter`，launcher 回 true 且 `ApplyRequested` → 自動套用。`TrayIconService`：`EnableCursorRequested`/`DisableCursorRequested`/`RestoreCursorRequested` 事件，三個選單項 `Enabled = true`。

- [ ] **Step 1: 寫失敗測試**

`MainViewModelTests.cs` 加 fake 與測試（`CreateVmWithStartup` helper 擴充兩個可選參數 `CursorService? cursorService = null, Func<byte[], string?>? confirmedCurWriter = null`，傳入建構子；default confirmedCurWriter 設 `_ => Path.Combine(_dir, "confirmed-cursor.cur")` 保證**所有**經 helper 的測試不落地真實 %AppData%）：

```csharp
    private sealed class FakeCursorService : CursorService
    {
        public bool ApplyResult = true;
        public bool RestoreResult = true;
        public List<string> Applied { get; } = new();
        public int Restored;

        public FakeCursorService()
            : base(_ => IntPtr.Zero, _ => false, () => false, _ => { },
                Path.Combine(Path.GetTempPath(), "MousePilotFakeMarker", Guid.NewGuid().ToString("N"), "m"))
        {
        }

        public override bool Apply(string curFilePath)
        {
            Applied.Add(curFilePath);
            if (ApplyResult) { IsApplied = true; }
            return ApplyResult;
        }

        public override bool Restore()
        {
            Restored++;
            if (RestoreResult) { IsApplied = false; }
            return RestoreResult;
        }
    }

    [Fact]
    public void 套用游標成功更新狀態與設定()
    {
        var cursor = new FakeCursorService();
        var vm = CreateVmWithStartup(new NoOpStartupService(), cursorService: cursor);
        vm.Settings.ConfirmedCursorFile = @"C:\appdata\confirmed-cursor.cur";
        vm.ApplyCursorCommand.NotifyCanExecuteChanged();

        Assert.True(vm.ApplyCursorCommand.CanExecute(null));
        vm.ApplyCursorCommand.Execute(null);

        Assert.Equal(new[] { @"C:\appdata\confirmed-cursor.cur" }, cursor.Applied);
        Assert.True(vm.Settings.CustomCursorEnabled);
        Assert.Equal("已套用自訂游標", vm.CursorStatusText);
    }

    [Fact]
    public void 套用失敗提示且不改設定()
    {
        var cursor = new FakeCursorService { ApplyResult = false };
        var vm = CreateVmWithStartup(new NoOpStartupService(), cursorService: cursor);
        vm.Settings.ConfirmedCursorFile = @"C:\appdata\confirmed-cursor.cur";

        vm.ApplyCursorCommand.Execute(null);

        Assert.False(vm.Settings.CustomCursorEnabled);
        Assert.Equal("Windows 預設", vm.CursorStatusText);
        Assert.Contains("失敗", vm.Notice);
    }

    [Fact]
    public void 恢復游標更新狀態與設定()
    {
        var cursor = new FakeCursorService();
        var vm = CreateVmWithStartup(new NoOpStartupService(), cursorService: cursor);
        vm.Settings.ConfirmedCursorFile = @"C:\a.cur";
        vm.ApplyCursorCommand.Execute(null);

        vm.RestoreCursorCommand.Execute(null);

        Assert.Equal(1, cursor.Restored);
        Assert.False(vm.Settings.CustomCursorEnabled);
        Assert.Equal("Windows 預設", vm.CursorStatusText);
    }

    [Fact]
    public void F10快捷鍵恢復游標()
    {
        var cursor = new FakeCursorService();
        var hotkeys = new HotkeyHarness();
        var vm = CreateVmWithStartup(new NoOpStartupService(), hotkeys.Service, cursorService: cursor);

        hotkeys.SimulatePress(2); // RestoreCursorHotkeyId

        Assert.Equal(1, cursor.Restored);
        Assert.Contains("已恢復", vm.Notice);
    }

    [Fact]
    public void 編輯器套用請求後自動套用()
    {
        var cursor = new FakeCursorService();
        var vm = CreateVmWithStartup(new NoOpStartupService(), cursorService: cursor);
        vm.AttachCursorEditorLauncher(editorVm =>
        {
            editorVm.SelectedSource = editorVm.Sources.First(s => s.Source.Id == "preset:Heart");
            editorVm.ConfirmAndApplyCommand.Execute(null);
            return true;
        });

        vm.EditCursorCommand.Execute(null);

        Assert.Single(cursor.Applied); // 確認落地路徑（test writer）被套用
        Assert.True(vm.Settings.CustomCursorEnabled);
    }
```

（注意 `F10快捷鍵恢復游標` 的 id 值：以檔內既有 `RestoreCursorHotkeyId` 常數為準——若非 2，用 `MainViewModel` 公開常數或既有測試同法。）

`TrayIconServiceTests.cs` 新增：

```csharp
    [Fact]
    public void 游標選單項啟用且觸發事件()
    {
        using var tray = new TrayIconService();
        var events = new List<string>();
        tray.EnableCursorRequested += () => events.Add("enable");
        tray.DisableCursorRequested += () => events.Add("disable");
        tray.RestoreCursorRequested += () => events.Add("restore");

        Assert.True(tray.FindMenuItem("啟用自訂游標")!.Enabled);
        tray.FindMenuItem("啟用自訂游標")!.PerformClick();
        tray.FindMenuItem("停用自訂游標")!.PerformClick();
        tray.FindMenuItem("恢復 Windows 游標")!.PerformClick();

        Assert.Equal(new[] { "enable", "disable", "restore" }, events);
    }
```

並把既有 `建構後選單項目與停用狀態正確` 測試中三個游標項的 `Assert.False(...Enabled)` 改為 `Assert.True`。

- [ ] **Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests` → 編譯失敗。

- [ ] **Step 3: 實作**

`Services/TrayIconService.cs`：三個 item `Enabled = false` 改 `true`（註解 `// Phase 9` 移除），新增事件並 wire：

```csharp
    public event Action? EnableCursorRequested;

    public event Action? DisableCursorRequested;

    public event Action? RestoreCursorRequested;
```

```csharp
        enableCursorItem.Click += (_, _) => EnableCursorRequested?.Invoke();
        disableCursorItem.Click += (_, _) => DisableCursorRequested?.Invoke();
        restoreCursorItem.Click += (_, _) => RestoreCursorRequested?.Invoke();
```

`ViewModels/MainViewModel.cs`：

1. 欄位 `private readonly CursorService _cursorService;`、`private readonly Func<byte[], string?>? _confirmedCurWriter;`；建構子加第 9/10 參數（見 Interfaces），`_cursorService = cursorService ?? new CursorService(); _confirmedCurWriter = confirmedCurWriter;`；建構子尾（RefreshCursorFileText 附近）加 `RefreshCursorStatusText();`。
2. 命令與刷新：

```csharp
    [RelayCommand(CanExecute = nameof(CanApplyCursor))]
    private void ApplyCursor()
    {
        if (_cursorService.Apply(Settings.ConfirmedCursorFile))
        {
            Settings.CustomCursorEnabled = true;
            SaveSettings();
            RefreshCursorStatusText();
            Notice = "已套用自訂游標。";
        }
        else
        {
            Notice = "套用游標失敗（檔案可能已損毀或被移除）。";
        }
    }

    private bool CanApplyCursor() => Settings.ConfirmedCursorFile.Length > 0;

    [RelayCommand]
    private void RestoreCursor()
    {
        if (_cursorService.Restore())
        {
            Settings.CustomCursorEnabled = false;
            SaveSettings();
            RefreshCursorStatusText();
            Notice = "已恢復 Windows 游標。";
        }
        else
        {
            Notice = "恢復游標失敗，將於下次啟動自動補救。";
        }
    }

    private void RefreshCursorStatusText()
        => CursorStatusText = _cursorService.IsApplied ? "已套用自訂游標" : "Windows 預設";
```

3. `OnHotkeyPressed` 的 F10 分支（移交 (a)）：

```csharp
        else if (id == RestoreCursorHotkeyId)
        {
            RestoreCursor();
        }
```

4. `EditCursor`：建立 VM 改 `var editorVm = new CursorEditorViewModel(Settings, confirmedWriter: _confirmedCurWriter);`；launcher 回 true 區塊尾加：

```csharp
            ApplyCursorCommand.NotifyCanExecuteChanged();
            if (editorVm.ApplyRequested)
            {
                ApplyCursor();
            }
```

`Views/MainWindow.xaml` 游標卡：兩個停用按鈕改為

```xml
                        <Button Content="套用" Command="{Binding ApplyCursorCommand}" Padding="12,6" Margin="0,0,8,0"/>
                        <Button Content="恢復 Windows 游標" Command="{Binding RestoreCursorCommand}" Padding="12,6"/>
```

卡片底部佔位文字 `預設圖案庫與預覽將於後續版本提供。` 那行 TextBlock 刪除；卡片註解 `<!-- Cursor 設定（Phase 7~9 實作，先佔位停用） -->` 改 `<!-- Cursor 設定 -->`。

- [ ] **Step 4: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`（236 + 5 + 1 = 242 全綠）→ `dotnet build -c Release` 0 警告。

- [ ] **Step 5: Commit**

```text
feat: 套用/恢復游標命令與 F10 及 Tray 選單啟用

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

### Task 4: App 整線——所有退出路徑掛恢復

**Files:**
- Modify: `App.xaml.cs`

**Interfaces:**
- Consumes: `CursorService`（App 建立、共享給 VM）、`MainViewModel.ApplyCursorCommand/RestoreCursorCommand`、Tray 三事件。

- [ ] **Step 1: 實作（`App.xaml.cs`）**

1. 欄位 `private CursorService? _cursorService;`。
2. `OnStartup` 在 `base.OnStartup(e);` 後、建 VM 前插入：

```csharp
        _cursorService = new CursorService();

        if (e.Args.Contains("--restore-cursor"))
        {
            _cursorService.Restore(); // 緊急補救參數（Spike B --restore-only 語意）：恢復後直接結束
            Shutdown();
            return;
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

（`e.Args.Contains` 需 `using System.Linq;`？——主專案 implicit usings 已含 Linq，不需新增。）

3. 建 VM 改 `var vm = new MainViewModel(new SettingsService(), cursorService: _cursorService);`。
4. Tray wiring 區加：

```csharp
        _tray.EnableCursorRequested += () => { if (vm.ApplyCursorCommand.CanExecute(null)) { vm.ApplyCursorCommand.Execute(null); } };
        _tray.DisableCursorRequested += () => vm.RestoreCursorCommand.Execute(null);
        _tray.RestoreCursorRequested += () => vm.RestoreCursorCommand.Execute(null);
```

5. `OnStartup` 尾（Show 邏輯前）加啟動自動套用：

```csharp
        if (vm.Settings.CustomCursorEnabled && vm.ApplyCursorCommand.CanExecute(null))
        {
            vm.ApplyCursorCommand.Execute(null); // 延續上次套用狀態（計畫決策 3）
        }
```

6. `ExitApplication`（§30 順序，步驟 5 插入）：

```csharp
        _exiting = true;
        _mainViewModel?.Dispose();      // 1~4：取消進行中移動、解除快捷鍵、停止輪詢 timer
        _cursorService?.Dispose();      // 5：恢復游標（已套用才動作）
        _tray?.Dispose();               // 6：系統匣圖示
        _mainViewModel?.SaveSettings(); // 7：保存設定
        Shutdown();                     // 9：關閉程式
```

7. `OnExit` 保險路徑 `_tray?.Dispose();` 後加 `_cursorService?.Dispose();`。

- [ ] **Step 2: Build + 測試 + 冒煙**

Run: `dotnet build -c Release`（0 警告）、`dotnet test tests/MousePilot.Tests`（242 綠）。
冒煙：背景啟動 exe → 4 秒存活 → Stop-Process；再跑 `MousePilot.exe --restore-cursor` 應立即結束（exit code 0）。

- [ ] **Step 3: Commit**

```text
feat: 全退出路徑掛游標恢復 - crash 補救/登出/例外/結束順序

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

### Task 5: Phase 收尾

**Files:**
- Modify: `CHANGELOG.md`、`docs/superpowers/plans/2026-08-20-mousepilot-master-plan.md`

- [ ] **Step 1: CHANGELOG [Unreleased]「### 新增」補上**

```markdown
- 全域游標套用/恢復（Phase 9）：SetSystemCursor 套用（僅標準箭頭）＋ SPI_SETCURSORS 恢復；恢復掛所有退出路徑（按鈕/F10/Tray/關閉/未處理例外/登出/crash 後啟動補救/--restore-cursor 參數）；「確定」落地 confirmed 游標檔（WYSIWYG）；Tray 游標選單項與主視窗 套用/恢復 按鈕啟用。
```

- [ ] **Step 2: Master Plan 更新**：Phase 9 列 ✅ 完成、細部計畫文件欄 `2026-08-23-phase9-global-cursor.md`。只動 Phase 9 列。

- [ ] **Step 3: 最終驗證**：`dotnet build -c Release` → `dotnet test tests/MousePilot.Tests`（242）→ `dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true`。

- [ ] **Step 4: Commit**

```text
docs: 更新 CHANGELOG 與進度總表 - Phase 9 完成

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

## Phase 9 完成定義

- [ ] build 0 error、測試全綠（預期 242）、publish 成功。
- [ ] 單元測試涵蓋：恢復先行（成功/失敗/marker 語意）、套用（檔案不存在/載入失敗/替換失敗銷毀 handle）、Dispose 保險、確定落地（三種來源/寫檔失敗）、套用/恢復命令、F10、編輯器套用續接、Tray 事件、Clamp 防護。
- [ ] **使用者實機手動驗證（§34 案例 20~22 + 規格 §11 全情境）——本 Phase 為最高風險，請務必逐項執行：**
  1. 編輯器選「愛心」→ 按「套用」→ **Windows 游標變成愛心**（案例 20）；移動到桌面/其他程式也生效。
  2. 主視窗「恢復 Windows 游標」→ 立即恢復原樣（案例 21）；再按「套用」→ 又變回。
  3. `Ctrl+Alt+F10` → 恢復。
  4. Tray：啟用自訂游標 / 停用自訂游標 / 恢復 Windows 游標 三項行為正確。
  5. 套用狀態下正常關閉程式 → 游標恢復（案例 22）；重開程式 → 自動套回（customCursorEnabled 延續）。
  6. 套用狀態下工作管理員強制結束 → 游標留在自訂（OS 限制）→ 再啟動 MousePilot → **啟動即補救恢復**再自動套回；或改跑 `MousePilot.exe --restore-cursor` 只恢復。
  7. 套用狀態下 Windows 登出/重開機 → 重新登入後游標正常（SessionEnding + 登入時 scheme 本就重載）。
  8. 把 confirmed-cursor.cur 手動刪除後按「套用」→ 失敗提示、不 crash。
