# MousePilot Phase 6：Global Hotkey 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 全域快捷鍵生效：預設 `Ctrl+Alt+F9` 切換啟動/暫停、`Ctrl+Alt+F10` 恢復游標（本階段註冊並接佔位提示，Phase 9 接真實動作）；UI 可按鍵擷取修改快捷鍵；無效/重複/被占用皆有清楚錯誤且還原。

**Architecture:** `HotkeyParser`（純函式："Ctrl+Alt+F9" ↔ 修飾鍵旗標+VK，含驗證）→ `HotkeyService`（RegisterHotKey 包裝：**隱藏 message-only HwndSource**（`HWND_MESSAGE` parent，不依賴可能藏在系統匣的 MainWindow——移交約束），註冊函式可注入供測試）→ VM（第五參數注入、兩個 hotkey 包裝屬性沿用 RunAtStartup 模式、啟動時註冊、F9 切換走 CanExecute）→ View（`HotkeyCapture` 純函式 + code-behind PreviewKeyDown 擷取）。另完成 Phase 4 移交：Tray Start/Pause 加 CanExecute guard。

**Tech Stack:** 既有。無新相依。

**Spec:** `docs/spec/mousepilot-spec.md`（§17、§21、§34 案例 26/27）；Master Plan Phase 6 章節（含三條移交事項）。

## Global Constraints

- Hotkey ID：`ToggleHotkeyId = 1`（啟動/暫停）、`RestoreCursorHotkeyId = 2`（恢復游標，Phase 9 接真實動作，本階段按下顯示佔位 Notice）。
- 組合字串正規格式：`[Ctrl+][Alt+][Shift+][Win+]<主鍵>`，順序固定 Ctrl→Alt→Shift→Win；主鍵支援 F1~F24、A~Z、0~9；必須至少一個修飾鍵 + 恰好一個主鍵。
- 驗證失敗/與另一項重複/RegisterHotKey 失敗（被占用）→ Notice 清楚錯誤 + 屬性還原（占用時舊組合重新註冊回去），程式不退出（規格 §17/§21）。
- 啟動時依 settings 註冊；任一註冊失敗 → Notice，程式照常運作。
- HwndSource 只在真實註冊路徑延遲建立（UI thread）；**測試一律注入 fake 註冊函式**——xUnit MTA 執行緒上建 HwndSource 會擲例外。
- **（Phase 5 教訓，硬性）**測試檔內每一個 `new MainViewModel(` 建構點必須明確注入全部服務參數（startupService **與** hotkeyService fake）。
- Phase 4 移交：App.xaml.cs 的 Tray Start/Pause lambda 加 `CanExecute` 檢查。
- VM.Dispose 需釋放 HotkeyService（§30 順序中的「Unregister Global Hotkey」步驟，位於 tray dispose 之前——現行 ExitApplication 呼叫 VM.Dispose 已在 Tray.Dispose 前，順序天然正確）。
- MainWindow code-behind 允許擴充：hotkey 擷取的 PreviewKeyDown 處理屬 View 層輸入邏輯（轉換邏輯抽在可測的 `HotkeyCapture` 純函式）。
- TDD；目前基準 117 綠。Commit 一律 `git commit -F <$env:TEMP 暫存檔>`（禁 here-string），繁中+前綴+`Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`，commit 後 `git log -1 --format=%B` 驗證。

---

### Task 1: HotkeyParser + HotkeyCapture（TDD 純函式）

**Files:**
- Create: `Services/HotkeyParser.cs`
- Create: `Views/HotkeyCapture.cs`
- Test: `tests/MousePilot.Tests/HotkeyParserTests.cs`、`tests/MousePilot.Tests/HotkeyCaptureTests.cs`

**Interfaces:**
- Produces（Task 2/3/4 依賴，逐字）:
  - `readonly record struct HotkeyCombo(uint Modifiers, uint VirtualKey)`
  - `static class HotkeyParser`：常數 `ModAlt=0x0001/ModControl=0x0002/ModShift=0x0004/ModWin=0x0008`；`static HotkeyCombo? Parse(string text)`（null=無效）；`static string? Validate(string text)`（錯誤訊息或 null）。
  - `static class MousePilot.Views.HotkeyCapture`：`static string? FromKeyEvent(Key key, ModifierKeys modifiers)`（null=僅修飾鍵/不支援的鍵/無修飾鍵）。

- [ ] **Step 1: 寫失敗測試**

`tests/MousePilot.Tests/HotkeyParserTests.cs`：

```csharp
using MousePilot.Services;

namespace MousePilot.Tests;

public class HotkeyParserTests
{
    [Theory]
    [InlineData("Ctrl+Alt+F9", HotkeyParser.ModControl | HotkeyParser.ModAlt, 0x78u)]
    [InlineData("Ctrl+Alt+F10", HotkeyParser.ModControl | HotkeyParser.ModAlt, 0x79u)]
    [InlineData("Shift+A", HotkeyParser.ModShift, 0x41u)]
    [InlineData("Win+0", HotkeyParser.ModWin, 0x30u)]
    [InlineData("Ctrl+Alt+Shift+Z", HotkeyParser.ModControl | HotkeyParser.ModAlt | HotkeyParser.ModShift, 0x5Au)]
    [InlineData("Ctrl+F24", HotkeyParser.ModControl, 0x87u)]
    public void 合法組合解析(string text, uint modifiers, uint vk)
    {
        var combo = HotkeyParser.Parse(text);
        Assert.NotNull(combo);
        Assert.Equal(modifiers, combo!.Value.Modifiers);
        Assert.Equal(vk, combo.Value.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("F9")]          // 無修飾鍵
    [InlineData("Ctrl+")]       // 無主鍵
    [InlineData("Ctrl+Alt")]    // 僅修飾鍵
    [InlineData("Ctrl+Esc")]    // 不支援的主鍵
    [InlineData("Ctrl+F25")]    // 超出 F24
    [InlineData("Ctrl+A+B")]    // 兩個主鍵
    public void 無效組合回傳null(string text)
    {
        Assert.Null(HotkeyParser.Parse(text));
    }

    [Fact]
    public void Validate對無效組合回傳錯誤訊息()
    {
        Assert.NotNull(HotkeyParser.Validate("F9"));
        Assert.Contains("修飾鍵", HotkeyParser.Validate("F9"));
    }

    [Fact]
    public void Validate對合法組合回傳null()
    {
        Assert.Null(HotkeyParser.Validate("Ctrl+Alt+F9"));
    }
}
```

`tests/MousePilot.Tests/HotkeyCaptureTests.cs`：

```csharp
using System.Windows.Input;
using MousePilot.Views;

namespace MousePilot.Tests;

public class HotkeyCaptureTests
{
    [Theory]
    [InlineData(Key.F9, ModifierKeys.Control | ModifierKeys.Alt, "Ctrl+Alt+F9")]
    [InlineData(Key.A, ModifierKeys.Control, "Ctrl+A")]
    [InlineData(Key.D5, ModifierKeys.Shift | ModifierKeys.Windows, "Shift+Win+5")]
    [InlineData(Key.F13, ModifierKeys.Alt, "Alt+F13")]
    public void 組合鍵轉為正規字串(Key key, ModifierKeys modifiers, string expected)
    {
        Assert.Equal(expected, HotkeyCapture.FromKeyEvent(key, modifiers));
    }

    [Theory]
    [InlineData(Key.F9, ModifierKeys.None)]              // 無修飾鍵
    [InlineData(Key.LeftCtrl, ModifierKeys.Control)]     // 修飾鍵本身
    [InlineData(Key.Escape, ModifierKeys.Control)]       // 不支援的主鍵
    public void 不完整或不支援的組合回傳null(Key key, ModifierKeys modifiers)
    {
        Assert.Null(HotkeyCapture.FromKeyEvent(key, modifiers));
    }
}
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 編譯失敗（型別不存在）。

- [ ] **Step 3: 實作**

`Services/HotkeyParser.cs`：

```csharp
namespace MousePilot.Services;

public readonly record struct HotkeyCombo(uint Modifiers, uint VirtualKey);

/// <summary>快捷鍵字串（"Ctrl+Alt+F9"）與 RegisterHotKey 參數的互轉（純函式）。</summary>
public static class HotkeyParser
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    public static HotkeyCombo? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        uint modifiers = 0;
        uint? vk = null;
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim();
            switch (part)
            {
                case "Ctrl":
                    modifiers |= ModControl;
                    continue;
                case "Alt":
                    modifiers |= ModAlt;
                    continue;
                case "Shift":
                    modifiers |= ModShift;
                    continue;
                case "Win":
                    modifiers |= ModWin;
                    continue;
            }

            if (KeyToVk(part) is not { } parsed || vk is not null)
            {
                return null; // 不支援的主鍵，或已有第二個主鍵
            }

            vk = parsed;
        }

        if (modifiers == 0 || vk is null)
        {
            return null; // 必須至少一個修飾鍵 + 恰好一個主鍵
        }

        return new HotkeyCombo(modifiers, vk.Value);
    }

    public static string? Validate(string text)
        => Parse(text) is null
            ? "快捷鍵格式無效：需要至少一個修飾鍵（Ctrl/Alt/Shift/Win）加一個主鍵（F1~F24、A~Z、0~9），例如 Ctrl+Alt+F9。"
            : null;

    private static uint? KeyToVk(string part)
    {
        if (part.Length >= 2 && part[0] == 'F' && int.TryParse(part[1..], out var f) && f is >= 1 and <= 24)
        {
            return 0x70u + (uint)(f - 1); // VK_F1 = 0x70
        }

        if (part.Length == 1)
        {
            var c = part[0];
            if (c is >= 'A' and <= 'Z')
            {
                return (uint)c; // VK_A~VK_Z 與 ASCII 相同
            }

            if (c is >= '0' and <= '9')
            {
                return (uint)c; // VK_0~VK_9 與 ASCII 相同
            }
        }

        return null;
    }
}
```

`Views/HotkeyCapture.cs`：

```csharp
using System.Windows.Input;

namespace MousePilot.Views;

/// <summary>把 WPF 按鍵事件轉為 "Ctrl+Alt+F9" 正規字串（純函式；順序固定 Ctrl→Alt→Shift→Win）。</summary>
public static class HotkeyCapture
{
    public static string? FromKeyEvent(Key key, ModifierKeys modifiers)
    {
        if (modifiers == ModifierKeys.None)
        {
            return null;
        }

        if (KeyName(key) is not { } name)
        {
            return null; // 修飾鍵本身或不支援的鍵
        }

        var parts = new List<string>(5);
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(name);
        return string.Join("+", parts);
    }

    private static string? KeyName(Key key) => key switch
    {
        >= Key.F1 and <= Key.F24 => $"F{key - Key.F1 + 1}",
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        _ => null,
    };
}
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 全綠（117 + parser 15（6+7 理論案例 + 2 facts）+ capture 7（4+3 理論案例）= 139；以無 FAIL 為準）。

- [ ] **Step 5: Commit**

```text
feat: HotkeyParser 與 HotkeyCapture - 快捷鍵字串互轉

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

### Task 2: HotkeyService + NativeMethods（TDD）

**Files:**
- Modify: `Native/NativeMethods.cs`（RegisterHotKey/UnregisterHotKey）
- Create: `Services/HotkeyService.cs`
- Test: `tests/MousePilot.Tests/HotkeyServiceTests.cs`

**Interfaces:**
- Produces（Task 3 依賴，逐字）:
  - `NativeMethods`：`internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);`、`internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);`（DllImport user32, SetLastError=true）
  - `sealed class HotkeyService : IDisposable`：建構子 `HotkeyService(Func<int, uint, uint, bool>? registerFn = null, Func<int, bool>? unregisterFn = null)`；`event Action<int>? HotkeyPressed`；`bool Register(int id, HotkeyCombo combo)`（同 id 重註冊會先解除舊的；失敗回傳 false 且不留殘留）；`void Unregister(int id)`；`void SimulatePress(int id)`（測試鉤：直接觸發事件）；`void Dispose()`（解除全部 + 釋放 HwndSource）。

- [ ] **Step 1: 寫失敗測試**

`tests/MousePilot.Tests/HotkeyServiceTests.cs`：

```csharp
using MousePilot.Services;

namespace MousePilot.Tests;

public class HotkeyServiceTests
{
    private sealed class FakeRegistrar
    {
        public List<(int Id, uint Mod, uint Vk)> Registered { get; } = new();
        public List<int> Unregistered { get; } = new();
        public bool NextResult = true;

        public bool Register(int id, uint mod, uint vk)
        {
            if (!NextResult)
            {
                return false;
            }

            Registered.Add((id, mod, vk));
            return true;
        }

        public bool Unregister(int id)
        {
            Unregistered.Add(id);
            return true;
        }
    }

    private static readonly HotkeyCombo Combo = new(HotkeyParser.ModControl | HotkeyParser.ModAlt, 0x78);

    [Fact]
    public void 註冊成功記錄組合()
    {
        var fake = new FakeRegistrar();
        using var service = new HotkeyService(fake.Register, fake.Unregister);
        Assert.True(service.Register(1, Combo));
        Assert.Equal((1, Combo.Modifiers, Combo.VirtualKey), fake.Registered.Single());
    }

    [Fact]
    public void 註冊失敗回傳false()
    {
        var fake = new FakeRegistrar { NextResult = false };
        using var service = new HotkeyService(fake.Register, fake.Unregister);
        Assert.False(service.Register(1, Combo));
    }

    [Fact]
    public void 同id重註冊會先解除舊的()
    {
        var fake = new FakeRegistrar();
        using var service = new HotkeyService(fake.Register, fake.Unregister);
        service.Register(1, Combo);
        service.Register(1, new HotkeyCombo(HotkeyParser.ModControl, 0x41));
        Assert.Equal(new[] { 1 }, fake.Unregistered);
        Assert.Equal(2, fake.Registered.Count);
    }

    [Fact]
    public void Dispose解除全部註冊()
    {
        var fake = new FakeRegistrar();
        var service = new HotkeyService(fake.Register, fake.Unregister);
        service.Register(1, Combo);
        service.Register(2, new HotkeyCombo(HotkeyParser.ModControl, 0x41));
        service.Dispose();
        Assert.Equal(new[] { 1, 2 }, fake.Unregistered.OrderBy(x => x));
    }

    [Fact]
    public void SimulatePress觸發事件()
    {
        var fake = new FakeRegistrar();
        using var service = new HotkeyService(fake.Register, fake.Unregister);
        var pressed = new List<int>();
        service.HotkeyPressed += pressed.Add;
        service.SimulatePress(2);
        Assert.Equal(new[] { 2 }, pressed);
    }
}
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 編譯失敗。

- [ ] **Step 3: 實作**

`Native/NativeMethods.cs` 新增（`DestroyIcon` 之後）：

```csharp
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);
```

`Services/HotkeyService.cs`：

```csharp
using System.Windows.Interop;
using MousePilot.Native;

namespace MousePilot.Services;

/// <summary>
/// 全域快捷鍵（規格 §17）。真實路徑掛在隱藏 message-only 視窗（HWND_MESSAGE），
/// 不依賴可能藏在系統匣的 MainWindow；註冊函式可注入供測試（xUnit MTA 無法建 HwndSource）。
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly Func<int, uint, uint, bool> _registerFn;
    private readonly Func<int, bool> _unregisterFn;
    private readonly Dictionary<int, HotkeyCombo> _registered = new();
    private HwndSource? _source;

    public event Action<int>? HotkeyPressed;

    public HotkeyService(Func<int, uint, uint, bool>? registerFn = null, Func<int, bool>? unregisterFn = null)
    {
        _registerFn = registerFn ?? ((id, mod, vk) => NativeMethods.RegisterHotKey(EnsureSource(), id, mod, vk));
        _unregisterFn = unregisterFn ?? (id => NativeMethods.UnregisterHotKey(EnsureSource(), id));
    }

    /// <summary>註冊（同 id 重註冊會先解除舊組合）。false = 被其他程式占用或失敗，且不留殘留註冊。</summary>
    public bool Register(int id, HotkeyCombo combo)
    {
        Unregister(id);
        if (!_registerFn(id, combo.Modifiers, combo.VirtualKey))
        {
            return false;
        }

        _registered[id] = combo;
        return true;
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id))
        {
            _unregisterFn(id);
        }
    }

    /// <summary>測試鉤：直接觸發 HotkeyPressed（WndProc 路徑無法在無訊息迴圈的測試中驅動）。</summary>
    public void SimulatePress(int id) => HotkeyPressed?.Invoke(id);

    private IntPtr EnsureSource()
    {
        if (_source is null)
        {
            var parameters = new HwndSourceParameters("MousePilotHotkeyWindow")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0,
                ParentWindow = HwndMessage,
            };
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
        }

        return _source.Handle;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            HotkeyPressed?.Invoke(wParam.ToInt32());
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _registered.Keys.ToList())
        {
            Unregister(id);
        }

        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
    }
}
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 全綠（139 + 5 = 144）。

- [ ] **Step 5: Commit**

```text
feat: HotkeyService - message-only 視窗與 RegisterHotKey 包裝

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

### Task 3: VM 整合 + Tray CanExecute guard（TDD）

**Files:**
- Modify: `ViewModels/MainViewModel.cs`
- Modify: `App.xaml.cs`（Tray Start/Pause lambda 加 CanExecute）
- Test: `tests/MousePilot.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `HotkeyService`/`HotkeyParser`/`HotkeyCombo`（Task 1/2）。
- Produces（Task 4 依賴）:
  - `MainViewModel`：常數 `public const int ToggleHotkeyId = 1; public const int RestoreCursorHotkeyId = 2;`；建構子第五參數 `HotkeyService? hotkeyService = null`；屬性 `string ToggleHotkeyText`、`string RestoreCursorHotkeyText`（包裝屬性：驗證→查重→重註冊→成功才更新 Settings，失敗 Notice+還原+舊組合重註冊）；建構時依 settings 註冊（失敗 Notice）；`HotkeyPressed`：id 1 → 依 Status 走 Start/Pause（**經 CanExecute**）、id 2 → 佔位 Notice；`Dispose` 釋放 hotkeyService。

- [ ] **Step 1: 寫失敗測試**

`tests/MousePilot.Tests/MainViewModelTests.cs`：

1. 新增共用 fake（放在 `NoOpStartupService` 旁）：

```csharp
    private sealed class HotkeyHarness
    {
        public List<(int Id, uint Mod, uint Vk)> Registered { get; } = new();
        public List<int> Unregistered { get; } = new();
        public HashSet<(uint Mod, uint Vk)> Occupied { get; } = new();
        public HotkeyService Service { get; }

        public HotkeyHarness()
        {
            Service = new HotkeyService(
                (id, mod, vk) =>
                {
                    if (Occupied.Contains((mod, vk)))
                    {
                        return false;
                    }

                    Registered.Add((id, mod, vk));
                    return true;
                },
                id => { Unregistered.Add(id); return true; });
        }
    }
```

2. 既有 helper 與直接建構點全部補第五參數：`CreateVm`/`CreateVmWithReturn`/`CreateVmWithStartup` 的 `new MainViewModel(...)` 加第 5 引數 `new HotkeyHarness().Service`（`CreateVmWithStartup` 改簽章為 `CreateVmWithStartup(StartupService startup, HotkeyService? hotkey = null)`，內部用 `hotkey ?? new HotkeyHarness().Service`）；三個直接建構點（游標讀取失敗/設定損毀/SaveSettings 不可寫）補 `, new HotkeyHarness().Service`。

3. 新增測試：

```csharp
    [Fact]
    public void 啟動時依設定註冊兩組快捷鍵()
    {
        var hotkeys = new HotkeyHarness();
        var vm = CreateVmWithStartup(new NoOpStartupService(), hotkeys.Service);
        Assert.Contains((MainViewModel.ToggleHotkeyId, HotkeyParser.ModControl | HotkeyParser.ModAlt, 0x78u), hotkeys.Registered);
        Assert.Contains((MainViewModel.RestoreCursorHotkeyId, HotkeyParser.ModControl | HotkeyParser.ModAlt, 0x79u), hotkeys.Registered);
        Assert.Equal("", vm.Notice);
    }

    [Fact]
    public void 啟動時快捷鍵被占用顯示提示但不當機()
    {
        var hotkeys = new HotkeyHarness();
        hotkeys.Occupied.Add((HotkeyParser.ModControl | HotkeyParser.ModAlt, 0x78u));
        var vm = CreateVmWithStartup(new NoOpStartupService(), hotkeys.Service);
        Assert.Contains("占用", vm.Notice);
    }

    [Fact]
    public void 修改快捷鍵成功後更新設定並重新註冊()
    {
        var hotkeys = new HotkeyHarness();
        var vm = CreateVmWithStartup(new NoOpStartupService(), hotkeys.Service);

        vm.ToggleHotkeyText = "Ctrl+Shift+M";

        Assert.Equal("Ctrl+Shift+M", vm.Settings.ToggleHotkey);
        Assert.Contains((MainViewModel.ToggleHotkeyId, HotkeyParser.ModControl | HotkeyParser.ModShift, 0x4Du), hotkeys.Registered);
    }

    [Fact]
    public void 無效快捷鍵顯示提示且還原()
    {
        var vm = CreateVmWithStartup(new NoOpStartupService(), new HotkeyHarness().Service);

        vm.ToggleHotkeyText = "F9";

        Assert.Equal("Ctrl+Alt+F9", vm.Settings.ToggleHotkey);
        Assert.Equal("Ctrl+Alt+F9", vm.ToggleHotkeyText);
        Assert.Contains("修飾鍵", vm.Notice);
    }

    [Fact]
    public void 與另一組重複顯示提示且還原()
    {
        var vm = CreateVmWithStartup(new NoOpStartupService(), new HotkeyHarness().Service);

        vm.ToggleHotkeyText = "Ctrl+Alt+F10"; // 與恢復游標重複

        Assert.Equal("Ctrl+Alt+F9", vm.Settings.ToggleHotkey);
        Assert.Contains("重複", vm.Notice);
    }

    [Fact]
    public void 被占用時顯示提示且舊組合重新註冊()
    {
        var hotkeys = new HotkeyHarness();
        var vm = CreateVmWithStartup(new NoOpStartupService(), hotkeys.Service);
        hotkeys.Occupied.Add((HotkeyParser.ModControl | HotkeyParser.ModShift, 0x4Du));

        vm.ToggleHotkeyText = "Ctrl+Shift+M";

        Assert.Equal("Ctrl+Alt+F9", vm.Settings.ToggleHotkey);
        Assert.Contains("占用", vm.Notice);
        // 舊組合在失敗後重新註冊（最後一筆應為 Ctrl+Alt+F9）
        Assert.Equal((MainViewModel.ToggleHotkeyId, HotkeyParser.ModControl | HotkeyParser.ModAlt, 0x78u), hotkeys.Registered[^1]);
    }

    [Fact]
    public void F9快捷鍵切換啟動與暫停()
    {
        var hotkeys = new HotkeyHarness();
        var vm = CreateVmWithStartup(new NoOpStartupService(), hotkeys.Service); // autoStart=false → Paused

        hotkeys.Service.SimulatePress(MainViewModel.ToggleHotkeyId);
        Assert.NotEqual(MonitorStatus.Paused, vm.Status);

        hotkeys.Service.SimulatePress(MainViewModel.ToggleHotkeyId);
        Assert.Equal(MonitorStatus.Paused, vm.Status);
    }

    [Fact]
    public void F10快捷鍵顯示佔位提示()
    {
        var hotkeys = new HotkeyHarness();
        var vm = CreateVmWithStartup(new NoOpStartupService(), hotkeys.Service);

        hotkeys.Service.SimulatePress(MainViewModel.RestoreCursorHotkeyId);

        Assert.Contains("自訂游標", vm.Notice);
    }
```

（注意：`CreateVmWithStartup` 的 settings JSON 為 `autoStartMonitoring:false`，F9 測試依此假設初始 Paused。）

- [ ] **Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 編譯失敗（VM 無第五參數/常數/屬性）。

- [ ] **Step 3: 實作（`ViewModels/MainViewModel.cs` 修改）**

1. 常數與欄位：

```csharp
    public const int ToggleHotkeyId = 1;
    public const int RestoreCursorHotkeyId = 2;

    private readonly HotkeyService _hotkeyService;
```

2. 建構子第五參數 `HotkeyService? hotkeyService = null`；建構子內（startup 同步區塊之後、`IdleService.Ticked += OnTicked;` 之前）加：

```csharp
        _hotkeyService = hotkeyService ?? new HotkeyService();
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
        RegisterHotkeyFromSettings(ToggleHotkeyId, Settings.ToggleHotkey, "啟動/暫停");
        RegisterHotkeyFromSettings(RestoreCursorHotkeyId, Settings.RestoreCursorHotkey, "恢復游標");
```

3. 新增方法與包裝屬性（放在 `RunAtStartup` 屬性之後）：

```csharp
    /// <summary>啟動/暫停快捷鍵（規格 §17）。</summary>
    public string ToggleHotkeyText
    {
        get => Settings.ToggleHotkey;
        set => TrySetHotkey(value, isToggle: true);
    }

    /// <summary>恢復 Windows 游標快捷鍵（動作於 Phase 9 接上）。</summary>
    public string RestoreCursorHotkeyText
    {
        get => Settings.RestoreCursorHotkey;
        set => TrySetHotkey(value, isToggle: false);
    }

    private void TrySetHotkey(string text, bool isToggle)
    {
        var current = isToggle ? Settings.ToggleHotkey : Settings.RestoreCursorHotkey;
        var propertyName = isToggle ? nameof(ToggleHotkeyText) : nameof(RestoreCursorHotkeyText);
        if (text == current)
        {
            return;
        }

        var error = HotkeyParser.Validate(text);
        var other = isToggle ? Settings.RestoreCursorHotkey : Settings.ToggleHotkey;
        if (error is null && text == other)
        {
            error = "快捷鍵不可與另一項重複。";
        }

        if (error is null)
        {
            var id = isToggle ? ToggleHotkeyId : RestoreCursorHotkeyId;
            var combo = HotkeyParser.Parse(text)!.Value;
            if (_hotkeyService.Register(id, combo))
            {
                if (isToggle)
                {
                    Settings.ToggleHotkey = text;
                }
                else
                {
                    Settings.RestoreCursorHotkey = text;
                }
            }
            else
            {
                error = $"快捷鍵 {text} 已被其他程式占用。";
                if (HotkeyParser.Parse(current) is { } old)
                {
                    _hotkeyService.Register(id, old); // 還原舊組合的註冊
                }
            }
        }

        if (error is not null)
        {
            Notice = error;
        }

        OnPropertyChanged(propertyName);
    }

    private void RegisterHotkeyFromSettings(int id, string text, string label)
    {
        if (HotkeyParser.Parse(text) is not { } combo)
        {
            Notice = $"快捷鍵設定「{text}」無效，{label} 快捷鍵未啟用。";
            return;
        }

        if (!_hotkeyService.Register(id, combo))
        {
            Notice = $"快捷鍵 {text} 已被其他程式占用，{label} 快捷鍵未啟用。";
        }
    }

    private void OnHotkeyPressed(int id)
    {
        if (id == ToggleHotkeyId)
        {
            if (Status == MonitorStatus.Paused)
            {
                if (StartCommand.CanExecute(null))
                {
                    StartCommand.Execute(null);
                }
            }
            else if (PauseCommand.CanExecute(null))
            {
                PauseCommand.Execute(null);
            }
        }
        else if (id == RestoreCursorHotkeyId)
        {
            Notice = "恢復 Windows 游標功能將於自訂游標功能完成後啟用。";
        }
    }
```

4. `Dispose()` 改為：

```csharp
    public void Dispose()
    {
        _moveCts?.Cancel();        // §30：結束時取消所有背景動作
        _hotkeyService.Dispose();  // §30 步驟 4：Unregister Global Hotkey
        IdleService.Dispose();
    }
```

5. `App.xaml.cs` 的兩行 Tray lambda 改為（Phase 4 移交）：

```csharp
        _tray.StartRequested += () => { if (vm.StartCommand.CanExecute(null)) { vm.StartCommand.Execute(null); } };
        _tray.PauseRequested += () => { if (vm.PauseCommand.CanExecute(null)) { vm.PauseCommand.Execute(null); } };
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`（144 + 8 = 152 全綠）、`dotnet build -c Release`（0 error 0 warning）。

- [ ] **Step 5: Commit**

```text
feat: 全域快捷鍵生效 - VM 註冊/切換/錯誤還原與 Tray guard

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

### Task 4: XAML 快捷鍵擷取 UI

**Files:**
- Modify: `Views/MainWindow.xaml`（系統設定卡加快捷鍵區）
- Modify: `Views/MainWindow.xaml.cs`（PreviewKeyDown 擷取 handler）

**Interfaces:**
- Consumes: `ToggleHotkeyText`/`RestoreCursorHotkeyText`（Task 3）、`HotkeyCapture.FromKeyEvent`（Task 1）。

- [ ] **Step 1: `Views/MainWindow.xaml` 系統設定卡末尾（最後一個 CheckBox 之後）加**

```xml
                    <TextBlock Text="快捷鍵（點擊欄位後按下想要的組合鍵）" FontWeight="SemiBold" Margin="0,10,0,6"/>
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto"/>
                            <ColumnDefinition Width="150"/>
                        </Grid.ColumnDefinitions>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="34"/>
                            <RowDefinition Height="34"/>
                        </Grid.RowDefinitions>

                        <TextBlock Grid.Row="0" Text="啟動 / 暫停" Style="{StaticResource FieldLabel}"/>
                        <TextBox Grid.Row="0" Grid.Column="1" VerticalAlignment="Center" IsReadOnly="True"
                                 Text="{Binding ToggleHotkeyText, Mode=OneWay}"
                                 PreviewKeyDown="OnToggleHotkeyKeyDown"/>

                        <TextBlock Grid.Row="1" Text="恢復 Windows 游標" Style="{StaticResource FieldLabel}"/>
                        <TextBox Grid.Row="1" Grid.Column="1" VerticalAlignment="Center" IsReadOnly="True"
                                 Text="{Binding RestoreCursorHotkeyText, Mode=OneWay}"
                                 PreviewKeyDown="OnRestoreHotkeyKeyDown"/>
                    </Grid>
```

- [ ] **Step 2: `Views/MainWindow.xaml.cs` 加擷取 handler（OnClosing 之後；轉換邏輯在可測的 HotkeyCapture，code-behind 只做事件轉接——計畫允許的 View 層輸入邏輯）**

```csharp
    private void OnToggleHotkeyKeyDown(object sender, KeyEventArgs e) => CaptureHotkey(e, isToggle: true);

    private void OnRestoreHotkeyKeyDown(object sender, KeyEventArgs e) => CaptureHotkey(e, isToggle: false);

    private void CaptureHotkey(KeyEventArgs e, bool isToggle)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key; // Alt 組合鍵的實際主鍵在 SystemKey
        if (key == Key.Tab)
        {
            return; // 放行 Tab/Shift+Tab 導航，避免鍵盤焦點陷阱（review 修正）
        }

        e.Handled = true;
        if (HotkeyCapture.FromKeyEvent(key, Keyboard.Modifiers) is not { } gesture)
        {
            return; // 僅修飾鍵或不支援的鍵：等待完整組合
        }

        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (isToggle)
        {
            vm.ToggleHotkeyText = gesture;
        }
        else
        {
            vm.RestoreCursorHotkeyText = gesture;
        }
    }
```

（檔頭需補 `using System.Windows.Input;` 與 `using MousePilot.ViewModels;` 若尚未存在。）

- [ ] **Step 3: Build + 測試 + 啟動冒煙**

Run: `dotnet build -c Release`（0 error 0 warning）、`dotnet test tests/MousePilot.Tests`（152 綠）。
啟動冒煙（無螢幕替代）：背景啟動、等 4 秒確認程序存活（StartMinimized 預設 true 無視窗）、Stop-Process。**真實快捷鍵按壓與 UI 擷取無法 headless 驗證**——如實標註留待使用者。

- [ ] **Step 4: Commit**

```text
feat: 快捷鍵擷取 UI - 點擊欄位按鍵設定

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

### Task 5: Phase 收尾

**Files:**
- Modify: `CHANGELOG.md`
- Modify: `docs/superpowers/plans/2026-08-20-mousepilot-master-plan.md`（Phase 6 → ✅ 完成、細部計畫文件欄填 `2026-08-21-phase6-global-hotkey.md`）

- [ ] **Step 1: CHANGELOG [Unreleased]「### 新增」補上**

```markdown
- 全域快捷鍵（Phase 6）：Ctrl+Alt+F9 切換啟動/暫停、Ctrl+Alt+F10 恢復游標（佔位，隨自訂游標功能啟用）；UI 點擊欄位按鍵即可修改，無效/重複/被占用皆有提示並還原；Tray 選單啟停加上狀態防護。
```

- [ ] **Step 2: Master Plan 更新（僅 Phase 6 列與細部計畫文件欄）**

- [ ] **Step 3: 最終驗證**

```powershell
dotnet build -c Release
dotnet test tests/MousePilot.Tests
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

- [ ] **Step 4: Commit**

```text
docs: 更新 CHANGELOG 與進度總表 - Phase 6 完成

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

## Phase 6 完成定義

- [ ] build 0 error、測試全綠（預期 152）、publish 成功。
- [ ] 單元測試涵蓋：字串互轉與驗證、擷取轉換、服務註冊/重註冊/Dispose、VM 啟動註冊/修改/無效/重複/占用還原/F9 切換/F10 佔位。
- [ ] **使用者實機手動驗證（規格 §34 案例 26/27）：**
  1. 程式在系統匣（視窗關閉也可）按 `Ctrl+Alt+F9` → 監控啟停切換（觀察 tray tooltip/選單狀態）（案例 26）。
  2. `Ctrl+Alt+F10` → Dashboard 顯示佔位提示。
  3. 點擊「啟動/暫停」欄位按 `Ctrl+Shift+M` → 欄位更新、`Ctrl+Shift+M` 生效、舊組合失效。
  4. 嘗試設成與另一項相同 → 顯示「重複」提示且欄位還原。
  5. 先開啟另一個佔用某組合的程式（或設成系統保留組合如 `Win+L` 不可用類型）→ 顯示「占用」提示且原組合仍生效（案例 27）。
  6. 重開程式 → 修改後的快捷鍵從 settings.json 載入並生效。
