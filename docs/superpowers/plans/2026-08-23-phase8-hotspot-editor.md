# MousePilot Phase 8：Hotspot Editor + Preview 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 游標編輯視窗全功能：預設圖案 Grid（16 內建 + 匯入檔案）+ 我的收藏、尺寸選擇（16~128）、點擊圖片設 Hotspot（十字標記/座標格/手動輸入）、JPG 去背開關與容差、雙背景模擬預覽面板（**面板局部 Cursor 真實預覽，絕不動全域**）；「確定」寫入設定。另完成 Phase 7 三條移交（孤兒檔案必修、左上角參考色、損毀 cur 匯入測試）。

**Architecture:** `CursorEditorViewModel`（本階段核心，全邏輯可測：來源清單/預設值/管線重建/退化偵測/收藏/確認寫入；`imageLoader`/`storedFilesProvider`/`cursorFactory` 全注入）+ `CursorEditorWindow`（薄 XAML/code-behind：點擊座標換算走純函式 `HotspotMath`）+ `BitmapInterop`（Drawing→WPF 影像橋，經 PNG 串流免 HBitmap 洩漏）。**預覽游標**用 WPF per-element `Cursor`（`new Cursor(stream)` 支援 .cur/.ani）掛在預覽面板上——真實外觀含 hotspot 行為，完全不碰 Windows 全域（規格 §10）。管線順序固定 **去背（左上角參考色）→ 裁切 → 縮放 → Write**（Phase 7 移交 (c)）。

**Tech Stack:** 既有。無新相依。

**Spec:** `docs/spec/mousepilot-spec.md`（§9/§10、補三~補八、§34 案例 18/19）；Master Plan Phase 8 章節（含 Phase 7 移交輸入 3 條）。

## 計畫決策（供使用者知悉，可否決）

- **.cur/.ani 來源「原樣使用」**：尺寸/去背/hotspot 控制項停用（hotspot 顯示檔內值），預覽直接以檔案位元組建 WPF Cursor——Phase 9 套用時走 `LoadCursorFromFile` 原檔。改造第三方 .cur 的編輯屬未來需求。
- **選擇語意互斥**：「確定」時 preset 與檔案二擇一（另一欄清空），避免 Phase 9 出現優先序歧義。匯入檔案永存 storage 目錄，隨時可在 Grid 重新選回。
- **匯入圖片預設尺寸 32**（無法自動判定卡通類；內建可愛類依 preset 資料預設 48，符合補五）。

## Global Constraints

- 預覽/編輯期間**絕不修改 Windows 全域 Cursor**（規格 §10）；「套用」按鈕存在但停用（Phase 9）。
- 管線順序：RemoveBackground（僅 ImageFile 且啟用時；參考色 = 原圖 `GetPixel(0,0)`，補六）→ TrimTransparent → ScaleProportional(選定尺寸) → CurFileFormat.Write(hotspot)。退化（裁切後 1×1 且原圖更大）→ Warning、不產生預覽游標。
- Hotspot 一律夾制在 `[0, size-1]`；預設：HotspotTopLeft 來源 (0,0)、其餘 (size/2, size/2)（補七）；尺寸變更時重新夾制。
- 尺寸集用 `AppSettings.AllowedCursorSizes`；容差 0~255、預設 30。
- 收藏 id 格式沿用 `preset:<Id>` / `file:<檔名>`（補八）；收藏寫入 `Settings.FavoriteCursors`。
- **Phase 7 移交必辦**：(1) `RemoveCursor` 檢查 `Remove` 回傳，失敗 → Notice 且**不清設定**；(2) 左上角參考色責任在本階段落實；(3) 補「損毀 .cur 經 Import 層」測試。
- WPF `Cursor` 建構注入（`cursorFactory`）——測試不觸發真實 Cursor 建立；VM 測試斷言 `CurrentCurBytes`。
- 測試檔內每個 `new MainViewModel(` 建構點維持全服務注入；新第八參數（editorLauncher）建構期無副作用，既有建構點不需補參數。
- TDD；目前基準 191 綠。Commit 一律 `git commit -F <$env:TEMP 暫存檔>`（禁 here-string），繁中+前綴+`Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`，commit 後 `git log -1 --format=%B` 驗證。**禁止對 docs/ 或非任務檔案做任何 git 還原操作；綠了才 commit。**

---

### Task 1: Phase 7 移交修復 + BitmapInterop + HotspotMath（TDD）

**Files:**
- Modify: `ViewModels/MainViewModel.cs`（RemoveCursor 孤兒修復）
- Create: `Views/BitmapInterop.cs`、`Views/HotspotMath.cs`
- Test: `tests/MousePilot.Tests/MainViewModelTests.cs`（+1 測試、FakeCursorImportService 加旗標）、`tests/MousePilot.Tests/CursorImportServiceTests.cs`（+1）、`tests/MousePilot.Tests/BitmapInteropTests.cs`（新）、`tests/MousePilot.Tests/HotspotMathTests.cs`（新）

**Interfaces:**
- Produces（Task 2/3 依賴，逐字）:
  - `static class MousePilot.Views.BitmapInterop`：`static BitmapSource ToBitmapSource(Bitmap bitmap)`（PNG 串流、OnLoad、Freeze）。
  - `static class MousePilot.Views.HotspotMath`：`static (int X, int Y) DisplayToPixel(double clickX, double clickY, double displaySize, int cursorSize)`（floor + 夾制 [0, size-1]）；`static double PixelToDisplayCenter(int pixel, double displaySize, int cursorSize)`（像素中心點）。

- [ ] **Step 1: 寫失敗測試**

`tests/MousePilot.Tests/MainViewModelTests.cs`：`FakeCursorImportService` 加 `public bool RemoveResult = true;`，其 `Remove` 覆寫改為 `{ Removed.Add(storedPath); return RemoveResult; }`；新增測試：

```csharp
    [Fact]
    public void 移除失敗時保留設定並提示()
    {
        var cursor = new FakeCursorImportService { RemoveResult = false };
        var vm = CreateVmWithCursor(cursor, @"C:\pics\cat.png");
        vm.ImportCursorCommand.Execute(null);

        vm.RemoveCursorCommand.Execute(null);

        Assert.Equal(@"C:\store\cat.png", vm.Settings.CursorFile); // 不清設定（Phase 7 移交修復）
        Assert.Contains("無法刪除", vm.Notice);
        Assert.True(vm.RemoveCursorCommand.CanExecute(null));
    }
```

`tests/MousePilot.Tests/CursorImportServiceTests.cs` 新增：

```csharp
    [Fact]
    public void 損毀cur回傳錯誤且不留殘檔()
    {
        var path = Path.Combine(_sourceDir, "bad.cur");
        File.WriteAllBytes(path, new byte[] { 0, 0, 9, 9, 1, 0 });

        var result = Create().Import(path);

        Assert.False(result.Success);
        Assert.Contains("CUR", result.Error);
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "store")));
    }
```

`tests/MousePilot.Tests/BitmapInteropTests.cs`：

```csharp
using System.Drawing;
using MousePilot.Views;

namespace MousePilot.Tests;

public class BitmapInteropTests
{
    [Fact]
    public void 轉換保留尺寸且已凍結()
    {
        using var bmp = new Bitmap(7, 5, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        bmp.SetPixel(0, 0, Color.Red);

        var source = BitmapInterop.ToBitmapSource(bmp);

        Assert.Equal(7, source.PixelWidth);
        Assert.Equal(5, source.PixelHeight);
        Assert.True(source.IsFrozen);
    }

    [Fact]
    public void 轉換後與原Bitmap獨立()
    {
        var bmp = new Bitmap(3, 3);
        var source = BitmapInterop.ToBitmapSource(bmp);
        bmp.Dispose(); // 原圖釋放後 BitmapSource 仍可用（OnLoad 已完整載入）

        Assert.Equal(3, source.PixelWidth);
    }
}
```

`tests/MousePilot.Tests/HotspotMathTests.cs`：

```csharp
using MousePilot.Views;

namespace MousePilot.Tests;

public class HotspotMathTests
{
    [Theory]
    [InlineData(0.0, 0.0, 256.0, 32, 0, 0)]
    [InlineData(255.9, 255.9, 256.0, 32, 31, 31)]   // 右下角 → 最後一格
    [InlineData(128.0, 8.0, 256.0, 32, 16, 1)]      // 每格 8px：128/8=16、8/8=1
    [InlineData(-5.0, 300.0, 256.0, 32, 0, 31)]     // 出界夾制
    public void 顯示座標轉像素(double cx, double cy, double display, int size, int ex, int ey)
    {
        Assert.Equal((ex, ey), HotspotMath.DisplayToPixel(cx, cy, display, size));
    }

    [Theory]
    [InlineData(0, 256.0, 32, 4.0)]    // 第 0 格中心 = 0.5/32*256
    [InlineData(31, 256.0, 32, 252.0)]
    public void 像素轉顯示中心點(int pixel, double display, int size, double expected)
    {
        Assert.Equal(expected, HotspotMath.PixelToDisplayCenter(pixel, display, size), 3);
    }
}
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 編譯失敗（BitmapInterop/HotspotMath 不存在、RemoveResult 欄位不存在）+ 移除失敗測試 FAIL。

- [ ] **Step 3: 實作**

`ViewModels/MainViewModel.cs` 的 `RemoveCursor` 改為：

```csharp
    [RelayCommand(CanExecute = nameof(CanRemoveCursor))]
    private void RemoveCursor()
    {
        if (!_cursorImportService.Remove(Settings.CursorFile))
        {
            Notice = "無法刪除游標檔案（可能被占用），設定未變更。"; // Phase 7 移交修復：失敗不清設定防孤兒
            return;
        }

        Settings.CursorFile = "";
        _lastImportSize = (null, null);
        RefreshCursorFileText();
        RemoveCursorCommand.NotifyCanExecuteChanged();
    }
```

`Views/BitmapInterop.cs`：

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;

namespace MousePilot.Views;

/// <summary>System.Drawing ↔ WPF 影像橋。</summary>
public static class BitmapInterop
{
    /// <summary>Bitmap → BitmapSource：經 PNG 串流（免 HBitmap 洩漏），OnLoad 完整載入後 Freeze（與來源 Bitmap 完全脫鉤）。</summary>
    public static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = ms;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
```

`Views/HotspotMath.cs`：

```csharp
namespace MousePilot.Views;

/// <summary>Hotspot 顯示座標 ↔ 游標像素座標換算（純函式；display 為正方形顯示區邊長）。</summary>
public static class HotspotMath
{
    public static (int X, int Y) DisplayToPixel(double clickX, double clickY, double displaySize, int cursorSize)
        => ((int)Math.Clamp(Math.Floor(clickX / displaySize * cursorSize), 0, cursorSize - 1),
            (int)Math.Clamp(Math.Floor(clickY / displaySize * cursorSize), 0, cursorSize - 1));

    public static double PixelToDisplayCenter(int pixel, double displaySize, int cursorSize)
        => (pixel + 0.5) / cursorSize * displaySize;
}
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 全綠（191 + 1 + 1 + 2 + 6 理論案例 = 201；以無 FAIL 為準）。

- [ ] **Step 5: Commit**

```text
fix: 移除失敗防孤兒與 Hotspot 座標換算橋接 - Phase 7 移交修復

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

### Task 2: CursorEditorViewModel（TDD，本階段核心）

**Files:**
- Create: `Models/CursorSource.cs`、`ViewModels/CursorEditorViewModel.cs`
- Modify: `Services/CursorImportService.cs`（新增 `ListStored()`）
- Test: `tests/MousePilot.Tests/CursorEditorViewModelTests.cs`；`tests/MousePilot.Tests/CursorImportServiceTests.cs`（+1）

**Interfaces:**
- Produces（Task 3/4 依賴，逐字）:
  - `enum CursorSourceKind { Preset, ImageFile, CursorFile }`；`sealed record CursorSource(string Id, string DisplayName, CursorSourceKind Kind, string? FilePath, bool HotspotTopLeft, int DefaultSize)`（Id：`preset:<GalleryId>` / `file:<檔名>`）。
  - `CursorImportService`：`public virtual IReadOnlyList<string> ListStored()`（storage 目錄內支援副檔名的完整路徑，目錄不存在回空；例外回空）。
  - `partial class CursorEditorViewModel : ObservableObject`：建構子 `CursorEditorViewModel(AppSettings settings, Func<string, Bitmap?>? imageLoader = null, Func<IReadOnlyList<string>>? storedFilesProvider = null, Func<byte[], System.Windows.Input.Cursor?>? cursorFactory = null)`；屬性 `ObservableCollection<CursorSourceItem> Sources`、`ObservableCollection<CursorSourceItem> FavoriteSources`、`CursorSourceItem? SelectedSource`、`int SelectedSize`、`int HotspotX/HotspotY`、`bool RemoveBackgroundEnabled`、`int Tolerance`（預設 30）、`ImageSource? PreviewImage`、`System.Windows.Input.Cursor? PreviewCursor`、`byte[]? CurrentCurBytes`、`string SourceSizeText`、`string Warning`、`bool CanEditProcessing`（Kind != CursorFile）、`bool CanRemoveBackground`（Kind == ImageFile）、`bool Confirmed`；`IReadOnlyList<int> AllowedSizes`；命令 `ConfirmCommand`（CanExecute=SelectedSource!=null）、`ToggleFavoriteCommand`；`void SetHotspot(int x, int y)`；事件 `event Action? CloseRequested`。
  - `partial class CursorSourceItem : ObservableObject`：`CursorSource Source`、`ImageSource? Thumbnail`、`[ObservableProperty] bool _isFavorite`、`string DisplayName => Source.DisplayName`。

- [ ] **Step 1: 寫失敗測試**

`tests/MousePilot.Tests/CursorImportServiceTests.cs` 新增：

```csharp
    [Fact]
    public void ListStored列出儲存目錄內支援的檔案()
    {
        var service = Create();
        Assert.Empty(service.ListStored()); // 目錄尚未建立

        var stored = service.Import(WritePng()).StoredPath!;
        File.WriteAllText(Path.Combine(_dir, "store", "note.txt"), "x"); // 不支援副檔名應排除

        Assert.Equal(new[] { stored }, service.ListStored());
    }
```

`tests/MousePilot.Tests/CursorEditorViewModelTests.cs`：

```csharp
using System.Drawing;
using System.IO;   // 測試專案 implicit usings 不含 System.IO
using MousePilot.Models;
using MousePilot.Services;
using MousePilot.ViewModels;

namespace MousePilot.Tests;

public sealed class CursorEditorViewModelTests : IDisposable
{
    private readonly List<Bitmap> _bitmaps = new();

    public void Dispose()
    {
        foreach (var bmp in _bitmaps)
        {
            bmp.Dispose();
        }
    }

    private Bitmap Track(Bitmap bmp)
    {
        _bitmaps.Add(bmp);
        return bmp;
    }

    private CursorEditorViewModel Create(
        AppSettings? settings = null,
        Func<string, Bitmap?>? imageLoader = null,
        IReadOnlyList<string>? storedFiles = null)
        => new(
            settings ?? new AppSettings(),
            imageLoader ?? (_ => Track(MakeOpaque(20, 10))),
            () => storedFiles ?? Array.Empty<string>(),
            _ => null); // 測試不建真實 WPF Cursor

    private static Bitmap MakeOpaque(int w, int h)
    {
        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(255, 0, 128, 255));
        return bmp;
    }

    [Fact]
    public void 來源清單含16內建與匯入檔案()
    {
        var vm = Create(storedFiles: new[] { @"C:\store\cat.png", @"C:\store\a.cur" });
        Assert.Equal(18, vm.Sources.Count);
        Assert.Equal(16, vm.Sources.Count(s => s.Source.Kind == CursorSourceKind.Preset));
        Assert.Contains(vm.Sources, s => s.Source.Id == "file:cat.png" && s.Source.Kind == CursorSourceKind.ImageFile);
        Assert.Contains(vm.Sources, s => s.Source.Id == "file:a.cur" && s.Source.Kind == CursorSourceKind.CursorFile);
    }

    [Fact]
    public void 選擇內建箭頭套用預設尺寸與左上Hotspot()
    {
        var vm = Create();
        vm.SelectedSource = vm.Sources.First(s => s.Source.Id == "preset:Arrow");

        Assert.Equal(32, vm.SelectedSize);
        Assert.Equal((0, 0), (vm.HotspotX, vm.HotspotY));
        Assert.NotNull(vm.CurrentCurBytes);
        Assert.NotNull(vm.PreviewImage);
    }

    [Fact]
    public void 選擇可愛類預設48與中心Hotspot()
    {
        var vm = Create();
        vm.SelectedSource = vm.Sources.First(s => s.Source.Id == "preset:CuteRobotCat");

        Assert.Equal(48, vm.SelectedSize);
        Assert.Equal((24, 24), (vm.HotspotX, vm.HotspotY));
    }

    [Fact]
    public void 產出cur內含Hotspot()
    {
        var vm = Create();
        vm.SelectedSource = vm.Sources.First(s => s.Source.Id == "preset:Heart");
        vm.SetHotspot(5, 7);

        var read = CurFileFormat.TryReadFirstImage(vm.CurrentCurBytes!);
        Assert.Equal((5, 7), (read!.Value.Info.HotspotX, read.Value.Info.HotspotY));
        read.Value.Image.Dispose();
    }

    [Fact]
    public void Hotspot夾制於尺寸範圍()
    {
        var vm = Create();
        vm.SelectedSource = vm.Sources.First(s => s.Source.Id == "preset:Dot");
        vm.SetHotspot(999, -3);
        Assert.Equal((31, 0), (vm.HotspotX, vm.HotspotY));

        vm.SelectedSize = 16; // 縮小尺寸 → 既有 hotspot 重新夾制
        Assert.Equal((15, 0), (vm.HotspotX, vm.HotspotY));
    }

    [Fact]
    public void 匯入圖片經管線縮放置中()
    {
        var vm = Create(storedFiles: new[] { @"C:\store\wide.png" }); // loader 給 20x10 不透明圖
        vm.SelectedSource = vm.Sources.First(s => s.Source.Id == "file:wide.png");

        Assert.Equal(32, vm.SelectedSize);
        Assert.Equal("20 x 10", vm.SourceSizeText);
        var read = CurFileFormat.TryReadFirstImage(vm.CurrentCurBytes!);
        Assert.Equal(32, read!.Value.Info.Width); // 縮放進 32x32 畫布
        read.Value.Image.Dispose();
    }

    [Fact]
    public void 去背使用左上角參考色()
    {
        var source = new Bitmap(4, 4, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(source))
        {
            g.Clear(Color.White);                    // 背景 = 左上角色
        }

        source.SetPixel(2, 2, Color.Red);            // 內容
        var vm = Create(imageLoader: _ => source, storedFiles: new[] { @"C:\store\jpg.jpg" });
        vm.SelectedSource = vm.Sources.First(s => s.Source.Id == "file:jpg.jpg");

        vm.RemoveBackgroundEnabled = true;           // 參考色取原圖 (0,0)=白 → 白色全透明、紅色保留

        var read = CurFileFormat.TryReadFirstImage(vm.CurrentCurBytes!);
        using var img = read!.Value.Image;
        var hasRed = false;
        for (var y = 0; y < img.Height && !hasRed; y++)
        {
            for (var x = 0; x < img.Width && !hasRed; x++)
            {
                hasRed = img.GetPixel(x, y).ToArgb() == Color.FromArgb(255, 255, 0, 0).ToArgb();
            }
        }

        Assert.True(hasRed);   // 紅點還在（且因裁切+縮放填滿畫布）
        Assert.Equal("", vm.Warning);
    }

    [Fact]
    public void 全背景圖去背後顯示退化警告()
    {
        var vm = Create(imageLoader: _ => Track(MakeOpaque(8, 8)), storedFiles: new[] { @"C:\store\bg.jpg" });
        vm.SelectedSource = vm.Sources.First(s => s.Source.Id == "file:bg.jpg");

        vm.RemoveBackgroundEnabled = true; // 全圖 = 左上角色 → 全透明 → 1×1 退化

        Assert.Contains("內容", vm.Warning);
        Assert.Null(vm.CurrentCurBytes);
        Assert.Null(vm.PreviewCursor);
    }

    [Fact]
    public void cur檔來源停用處理控制()
    {
        using var bmp = MakeOpaque(8, 8);
        var curBytes = CurFileFormat.Write(bmp, 2, 3);
        var dir = Path.Combine(Path.GetTempPath(), "MousePilotEditorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var curPath = Path.Combine(dir, "x.cur");
        File.WriteAllBytes(curPath, curBytes);
        try
        {
            var vm = Create(storedFiles: new[] { curPath });
            vm.SelectedSource = vm.Sources.First(s => s.Source.Kind == CursorSourceKind.CursorFile);

            Assert.False(vm.CanEditProcessing);
            Assert.False(vm.CanRemoveBackground);
            Assert.Equal((2, 3), (vm.HotspotX, vm.HotspotY)); // 顯示檔內 hotspot
            Assert.Equal(curBytes, vm.CurrentCurBytes);        // 原樣位元組
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void 載入失敗顯示警告不當機()
    {
        var vm = Create(imageLoader: _ => null, storedFiles: new[] { @"C:\store\gone.png" });
        vm.SelectedSource = vm.Sources.First(s => s.Source.Kind == CursorSourceKind.ImageFile);

        Assert.Contains("載入", vm.Warning);
        Assert.Null(vm.CurrentCurBytes);
    }

    [Fact]
    public void 收藏切換寫入設定並更新收藏清單()
    {
        var settings = new AppSettings();
        var vm = Create(settings);
        vm.SelectedSource = vm.Sources.First(s => s.Source.Id == "preset:Heart");

        vm.ToggleFavoriteCommand.Execute(null);
        Assert.Contains("preset:Heart", settings.FavoriteCursors);
        Assert.Single(vm.FavoriteSources);
        Assert.True(vm.SelectedSource!.IsFavorite);

        vm.ToggleFavoriteCommand.Execute(null);
        Assert.Empty(settings.FavoriteCursors);
        Assert.Empty(vm.FavoriteSources);
    }

    [Fact]
    public void 確認選擇內建圖案寫入設定並互斥清空檔案()
    {
        var settings = new AppSettings { CursorFile = @"C:\store\old.png" };
        var vm = Create(settings);
        vm.SelectedSource = vm.Sources.First(s => s.Source.Id == "preset:Star");
        vm.SelectedSize = 48;
        vm.SetHotspot(10, 12);
        var closed = false;
        vm.CloseRequested += () => closed = true;

        vm.ConfirmCommand.Execute(null);

        Assert.Equal("Star", settings.CursorPreset);
        Assert.Equal("", settings.CursorFile);       // 互斥清空
        Assert.Equal(48, settings.CursorSize);
        Assert.Equal((10, 12), (settings.CursorHotspotX, settings.CursorHotspotY));
        Assert.True(vm.Confirmed);
        Assert.True(closed);
    }

    [Fact]
    public void 確認選擇檔案寫入設定並互斥清空Preset()
    {
        var settings = new AppSettings { CursorPreset = "Arrow" };
        var vm = Create(settings, storedFiles: new[] { @"C:\store\cat.png" });
        vm.SelectedSource = vm.Sources.First(s => s.Source.Kind == CursorSourceKind.ImageFile);

        vm.ConfirmCommand.Execute(null);

        Assert.Equal(@"C:\store\cat.png", settings.CursorFile);
        Assert.Equal("", settings.CursorPreset);
    }

    [Fact]
    public void 未選擇時確認命令停用()
    {
        Assert.False(Create().ConfirmCommand.CanExecute(null));
    }
}
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 編譯失敗。

- [ ] **Step 3: 實作**

`Services/CursorImportService.cs` 新增（`Remove` 之後）：

```csharp
    /// <summary>列出儲存目錄內支援副檔名的檔案（完整路徑）；目錄不存在或讀取失敗回空清單。</summary>
    public virtual IReadOnlyList<string> ListStored()
    {
        try
        {
            if (!Directory.Exists(_storageDir))
            {
                return Array.Empty<string>();
            }

            return Directory.GetFiles(_storageDir)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
```

`Models/CursorSource.cs`：

```csharp
namespace MousePilot.Models;

public enum CursorSourceKind
{
    Preset,
    ImageFile,
    CursorFile,
}

/// <summary>游標編輯器的來源項（Id 格式沿用收藏：preset:&lt;GalleryId&gt; / file:&lt;檔名&gt;，規格補八）。</summary>
public sealed record CursorSource(
    string Id,
    string DisplayName,
    CursorSourceKind Kind,
    string? FilePath,
    bool HotspotTopLeft,
    int DefaultSize);
```

`ViewModels/CursorEditorViewModel.cs`：

```csharp
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MousePilot.Models;
using MousePilot.Services;
using MousePilot.Views;

namespace MousePilot.ViewModels;

public partial class CursorSourceItem : ObservableObject
{
    public CursorSourceItem(CursorSource source, ImageSource? thumbnail, bool isFavorite)
    {
        Source = source;
        Thumbnail = thumbnail;
        _isFavorite = isFavorite;
    }

    public CursorSource Source { get; }

    public ImageSource? Thumbnail { get; }

    [ObservableProperty]
    private bool _isFavorite;

    public string DisplayName => Source.DisplayName;
}

/// <summary>
/// 游標編輯器（規格 §9/§10/補三~補八）。全邏輯可測：影像來源、WPF Cursor 建立、檔案列舉皆注入。
/// 管線順序固定：去背（左上角參考色）→ 裁切 → 縮放 → Write（Phase 7 移交約束）。預覽絕不動全域游標。
/// </summary>
public partial class CursorEditorViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private readonly Func<string, Bitmap?> _imageLoader;
    private readonly Func<byte[], System.Windows.Input.Cursor?> _cursorFactory;
    private bool _applyingDefaults;
    private Bitmap? _loadedSource;   // 原圖快取：同來源反覆 Rebuild 不重複解碼、防 GDI handle 洩漏（review 修正）
    private string? _loadedSourcePath;

    public event Action? CloseRequested;

    public CursorEditorViewModel(
        AppSettings settings,
        Func<string, Bitmap?>? imageLoader = null,
        Func<IReadOnlyList<string>>? storedFilesProvider = null,
        Func<byte[], System.Windows.Input.Cursor?>? cursorFactory = null)
    {
        _settings = settings;
        _imageLoader = imageLoader ?? LoadBitmap;
        _cursorFactory = cursorFactory ?? CreateCursor;
        var storedFiles = (storedFilesProvider ?? DefaultStoredFiles)();

        Sources = new ObservableCollection<CursorSourceItem>(BuildSources(storedFiles));
        FavoriteSources = new ObservableCollection<CursorSourceItem>(Sources.Where(s => s.IsFavorite));
    }

    public ObservableCollection<CursorSourceItem> Sources { get; }

    public ObservableCollection<CursorSourceItem> FavoriteSources { get; }

    public IReadOnlyList<int> AllowedSizes => AppSettings.AllowedCursorSizes;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private CursorSourceItem? _selectedSource;

    [ObservableProperty]
    private int _selectedSize = 32;

    [ObservableProperty]
    private int _hotspotX;

    [ObservableProperty]
    private int _hotspotY;

    [ObservableProperty]
    private bool _removeBackgroundEnabled;

    [ObservableProperty]
    private int _tolerance = 30;

    [ObservableProperty]
    private ImageSource? _previewImage;

    [ObservableProperty]
    private System.Windows.Input.Cursor? _previewCursor;

    [ObservableProperty]
    private byte[]? _currentCurBytes;

    [ObservableProperty]
    private string _sourceSizeText = "—";

    [ObservableProperty]
    private string _warning = "";

    public bool CanEditProcessing => SelectedSource?.Source.Kind != CursorSourceKind.CursorFile && SelectedSource is not null;

    public bool CanRemoveBackground => SelectedSource?.Source.Kind == CursorSourceKind.ImageFile;

    public bool Confirmed { get; private set; }

    public void SetHotspot(int x, int y)
    {
        HotspotX = Math.Clamp(x, 0, SelectedSize - 1);
        HotspotY = Math.Clamp(y, 0, SelectedSize - 1);
    }

    partial void OnSelectedSourceChanged(CursorSourceItem? value)
    {
        if (value is null)
        {
            return;
        }

        _applyingDefaults = true;
        SelectedSize = value.Source.DefaultSize;
        RemoveBackgroundEnabled = false;
        var center = SelectedSize / 2;
        HotspotX = value.Source.HotspotTopLeft ? 0 : center;
        HotspotY = value.Source.HotspotTopLeft ? 0 : center;
        _applyingDefaults = false;
        OnPropertyChanged(nameof(CanEditProcessing));
        OnPropertyChanged(nameof(CanRemoveBackground));
        Rebuild();
    }

    partial void OnSelectedSizeChanged(int value)
    {
        if (_applyingDefaults)
        {
            return;
        }

        SetHotspot(HotspotX, HotspotY); // 重新夾制
        Rebuild();
    }

    partial void OnHotspotXChanged(int value)
    {
        if (!_applyingDefaults)
        {
            Rebuild();
        }
    }

    partial void OnHotspotYChanged(int value)
    {
        if (!_applyingDefaults)
        {
            Rebuild();
        }
    }

    partial void OnRemoveBackgroundEnabledChanged(bool value)
    {
        if (!_applyingDefaults)
        {
            Rebuild();
        }
    }

    partial void OnToleranceChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 255);
        if (clamped != value)
        {
            Tolerance = clamped; // 夾制後重新觸發本 handler 一次（等值防重入）
            return;
        }

        if (!_applyingDefaults)
        {
            Rebuild();
        }
    }

    [RelayCommand]
    private void ToggleFavorite()
    {
        if (SelectedSource is not { } item)
        {
            return;
        }

        item.IsFavorite = !item.IsFavorite;
        if (item.IsFavorite)
        {
            _settings.FavoriteCursors.Add(item.Source.Id);
            FavoriteSources.Add(item);
        }
        else
        {
            _settings.FavoriteCursors.Remove(item.Source.Id);
            FavoriteSources.Remove(item);
        }
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        var source = SelectedSource!.Source;
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

    private bool CanConfirm() => SelectedSource is not null;

    private void Rebuild()
    {
        Warning = "";
        CurrentCurBytes = null;
        PreviewCursor = null;
        PreviewImage = null;
        SourceSizeText = "—";
        if (SelectedSource is not { } item)
        {
            return;
        }

        try
        {
            switch (item.Source.Kind)
            {
                case CursorSourceKind.Preset:
                {
                    using var rendered = CursorGallery.Render(item.Source.Id["preset:".Length..], SelectedSize);
                    FinishBuild(rendered);
                    break;
                }

                case CursorSourceKind.ImageFile:
                    RebuildFromImageFile(item.Source.FilePath!);
                    break;

                case CursorSourceKind.CursorFile:
                    RebuildFromCursorFile(item.Source.FilePath!);
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or OutOfMemoryException)
        {
            Warning = $"預覽建立失敗：{ex.Message}"; // 規格 §21：不 crash
        }
    }

    /// <summary>按路徑快取 loader 載入的原圖：同一來源反覆 Rebuild 不重複解碼；換來源時釋放舊圖（review 修正）。</summary>
    private Bitmap? LoadSourceCached(string path)
    {
        if (_loadedSourcePath != path || _loadedSource is null)
        {
            _loadedSource?.Dispose();
            _loadedSource = _imageLoader(path);
            _loadedSourcePath = path;
        }

        return _loadedSource;
    }

    public void Dispose()
    {
        _loadedSource?.Dispose();
        _loadedSource = null;
    }

    private void RebuildFromImageFile(string path)
    {
        // 不 Dispose：來源由 _loadedSource 快取持有所有權，換來源或 VM Dispose 時才釋放。
        var source = LoadSourceCached(path);
        if (source is null)
        {
            Warning = "圖片載入失敗（檔案可能已被移除或損毀）。";
            return;
        }

        SourceSizeText = $"{source.Width} x {source.Height}";
        Bitmap working = source;
        Bitmap? removed = null;
        try
        {
            if (RemoveBackgroundEnabled)
            {
                // 補六：預設參考色 = 原圖左上角像素（任何處理前取得）
                removed = CursorImageProcessor.RemoveBackground(source, source.GetPixel(0, 0), Tolerance);
                working = removed;
            }

            using var trimmed = CursorImageProcessor.TrimTransparent(working);
            // TrimTransparent 對「全透明」與「僅剩一個實心像素」都會收斂成 1x1，需再檢查該像素是否透明才能分辨（review 修正）。
            if (trimmed.Width == 1 && trimmed.Height == 1 && (source.Width > 1 || source.Height > 1) && trimmed.GetPixel(0, 0).A == 0)
            {
                Warning = "去背後沒有可見內容——請降低容差或關閉去背。"; // 退化防護（Phase 7 移交 (d) 前半）
                return;
            }

            // 1x1 內容直接實心填滿：GDI+ HighQualityBicubic 對 1x1 來源放大時無法達到完全不透明（實測 alpha 上限約 190/255），
            // ScaleProportional 本身即有此限制，故此處繞過內插改直接填色（review 修正）。
            using var scaled = trimmed.Width == 1 && trimmed.Height == 1
                ? FillSolid(trimmed.GetPixel(0, 0), SelectedSize)
                : CursorImageProcessor.ScaleProportional(trimmed, SelectedSize);
            FinishBuild(scaled);
        }
        finally
        {
            removed?.Dispose();
        }
    }

    private static Bitmap FillSolid(System.Drawing.Color color, int size)
    {
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(color);
        return bmp;
    }

    private void RebuildFromCursorFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var read = Path.GetExtension(path).ToLowerInvariant() == ".ani"
            ? CurFileFormat.TryReadAniFirstFrame(bytes)
            : CurFileFormat.TryReadFirstImage(bytes);
        if (read is { } ok)
        {
            SourceSizeText = $"{ok.Info.Width} x {ok.Info.Height}";
            _applyingDefaults = true;
            HotspotX = ok.Info.HotspotX; // 顯示檔內 hotspot（唯讀語意，控制項停用）
            HotspotY = ok.Info.HotspotY;
            _applyingDefaults = false;
            using var img = ok.Image;
            PreviewImage = BitmapInterop.ToBitmapSource(img);
        }
        else
        {
            Warning = "無法解析游標檔預覽（Windows 可能仍可套用）。";
        }

        CurrentCurBytes = bytes; // 原樣使用（Phase 9 走 LoadCursorFromFile）
        PreviewCursor = _cursorFactory(bytes);
    }

    private void FinishBuild(Bitmap finalBitmap)
    {
        CurrentCurBytes = CurFileFormat.Write(finalBitmap, HotspotX, HotspotY);
        PreviewImage = BitmapInterop.ToBitmapSource(finalBitmap);
        PreviewCursor = _cursorFactory(CurrentCurBytes);
    }

    private IEnumerable<CursorSourceItem> BuildSources(IReadOnlyList<string> storedFiles)
    {
        foreach (var preset in CursorGallery.Presets)
        {
            var id = $"preset:{preset.Id}";
            using var thumb = CursorGallery.Render(preset.Id, 32);
            yield return new CursorSourceItem(
                new CursorSource(id, preset.DisplayName, CursorSourceKind.Preset, null, preset.HotspotTopLeft, preset.DefaultSize),
                BitmapInterop.ToBitmapSource(thumb),
                _settings.FavoriteCursors.Contains(id));
        }

        foreach (var path in storedFiles)
        {
            var name = Path.GetFileName(path);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var kind = ext is ".cur" or ".ani" ? CursorSourceKind.CursorFile : CursorSourceKind.ImageFile;
            var id = $"file:{name}";
            yield return new CursorSourceItem(
                new CursorSource(id, name, kind, path, HotspotTopLeft: false, DefaultSize: 32),
                thumbnail: null,
                _settings.FavoriteCursors.Contains(id));
        }
    }

    private static Bitmap? LoadBitmap(string path)
    {
        try
        {
            return new Bitmap(path);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or OutOfMemoryException
            or System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }

    private static System.Windows.Input.Cursor? CreateCursor(byte[] curBytes)
    {
        try
        {
            using var ms = new MemoryStream(curBytes);
            return new System.Windows.Input.Cursor(ms, scaleWithDpi: true);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or FormatException)
        {
            return null; // 預覽游標建立失敗 → 僅無游標預覽，不影響其他預覽
        }
    }

    private static IReadOnlyList<string> DefaultStoredFiles() => new CursorImportService().ListStored();
}
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 全綠（201 + 1 + 14 = 216；以無 FAIL 為準）。

- [ ] **Step 5: Commit**

```text
feat: CursorEditorViewModel - 來源/管線/收藏/確認核心邏輯

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

### Task 3: CursorEditorWindow（XAML + code-behind）

**Files:**
- Create: `Views/CursorEditorWindow.xaml`、`Views/CursorEditorWindow.xaml.cs`

**Interfaces:**
- Consumes: `CursorEditorViewModel` 全部（Task 2）、`HotspotMath`（Task 1）。
- Produces: `CursorEditorWindow`（DataContext = CursorEditorViewModel；`CloseRequested` → DialogResult=true 關閉）。

- [ ] **Step 1: `Views/CursorEditorWindow.xaml`**

```xml
<Window x:Class="MousePilot.Views.CursorEditorWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="游標編輯"
        Width="900" Height="620"
        MinWidth="820" MinHeight="560"
        WindowStartupLocation="CenterOwner"
        Background="#F3F4F6">
    <Grid Margin="12">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="230"/>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="300"/>
        </Grid.ColumnDefinitions>

        <!-- 左：來源選擇（收藏 + 全部） -->
        <Border Grid.Column="0" Style="{StaticResource Card}" Margin="0,0,12,0">
            <ScrollViewer VerticalScrollBarVisibility="Auto">
                <StackPanel>
                    <TextBlock Text="我的收藏" Style="{StaticResource CardTitle}"/>
                    <ItemsControl ItemsSource="{Binding FavoriteSources}" Margin="0,0,0,10">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Button Content="{Binding DisplayName}" Padding="8,4" Margin="0,2"
                                        HorizontalContentAlignment="Left"
                                        Click="OnSourceClicked"/>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <TextBlock Text="全部圖案" Style="{StaticResource CardTitle}"/>
                    <ItemsControl ItemsSource="{Binding Sources}">
                        <ItemsControl.ItemsPanel>
                            <ItemsPanelTemplate>
                                <WrapPanel/>
                            </ItemsPanelTemplate>
                        </ItemsControl.ItemsPanel>
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Button Width="96" Height="76" Margin="2" Click="OnSourceClicked"
                                        ToolTip="{Binding DisplayName}">
                                    <StackPanel>
                                        <Image Source="{Binding Thumbnail}" Width="32" Height="32"
                                               RenderOptions.BitmapScalingMode="NearestNeighbor"/>
                                        <TextBlock Text="{Binding DisplayName}" FontSize="10"
                                                   TextTrimming="CharacterEllipsis" HorizontalAlignment="Center"/>
                                    </StackPanel>
                                </Button>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </ScrollViewer>
        </Border>

        <!-- 中：編輯區 -->
        <Border Grid.Column="1" Style="{StaticResource Card}" Margin="0,0,12,0">
            <StackPanel>
                <TextBlock Text="編輯" Style="{StaticResource CardTitle}"/>
                <Border Width="256" Height="256" HorizontalAlignment="Left"
                        BorderBrush="#D1D5DB" BorderThickness="1" Background="#FFFFFF">
                    <Grid>
                        <Image x:Name="EditImage" Source="{Binding PreviewImage}"
                               Width="256" Height="256" Stretch="Uniform"
                               RenderOptions.BitmapScalingMode="NearestNeighbor"
                               MouseLeftButtonDown="OnEditImageClicked"/>
                        <Canvas x:Name="HotspotOverlay" IsHitTestVisible="False"/>
                    </Grid>
                </Border>
                <TextBlock Margin="0,6,0,0" Foreground="#6B7280">
                    <Run Text="原始尺寸："/><Run Text="{Binding SourceSizeText, Mode=OneWay}"/>
                    <Run Text="　實際使用："/><Run Text="{Binding SelectedSize, Mode=OneWay}"/><Run Text=" px"/>
                </TextBlock>
                <Grid Margin="0,8,0,0">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="110"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="60"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="60"/>
                    </Grid.ColumnDefinitions>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="32"/>
                        <RowDefinition Height="32"/>
                    </Grid.RowDefinitions>
                    <TextBlock Text="尺寸" Style="{StaticResource FieldLabel}"/>
                    <ComboBox Grid.Column="1" VerticalAlignment="Center"
                              ItemsSource="{Binding AllowedSizes}"
                              SelectedItem="{Binding SelectedSize}"
                              IsEnabled="{Binding CanEditProcessing}"/>
                    <TextBlock Grid.Column="2" Text="Hotspot X" Style="{StaticResource FieldLabel}" Margin="12,0,8,0"/>
                    <TextBox Grid.Column="3" VerticalAlignment="Center"
                             Text="{Binding HotspotX, UpdateSourceTrigger=PropertyChanged}"
                             IsEnabled="{Binding CanEditProcessing}"/>
                    <TextBlock Grid.Column="4" Text="Y" Style="{StaticResource FieldLabel}" Margin="12,0,8,0"/>
                    <TextBox Grid.Column="5" VerticalAlignment="Center"
                             Text="{Binding HotspotY, UpdateSourceTrigger=PropertyChanged}"
                             IsEnabled="{Binding CanEditProcessing}"/>
                    <CheckBox Grid.Row="1" Grid.ColumnSpan="3" VerticalAlignment="Center"
                              Content="移除背景（JPG 建議）"
                              IsChecked="{Binding RemoveBackgroundEnabled}"
                              IsEnabled="{Binding CanRemoveBackground}"/>
                    <TextBlock Grid.Row="1" Grid.Column="3" Text="容差" Style="{StaticResource FieldLabel}" Margin="12,0,8,0" HorizontalAlignment="Right"/>
                    <TextBox Grid.Row="1" Grid.Column="5" VerticalAlignment="Center"
                             Text="{Binding Tolerance, UpdateSourceTrigger=PropertyChanged}"
                             IsEnabled="{Binding CanRemoveBackground}"/>
                </Grid>
                <TextBlock Text="{Binding Warning}" Foreground="#DC2626" TextWrapping="Wrap" Margin="0,8,0,0"/>
                <StackPanel Orientation="Horizontal" Margin="0,10,0,0">
                    <Button Content="加入 / 移除收藏" Command="{Binding ToggleFavoriteCommand}" Padding="12,6" Margin="0,0,8,0"/>
                    <Button Content="確定" Command="{Binding ConfirmCommand}" Padding="16,6" Margin="0,0,8,0"/>
                    <Button Content="套用" IsEnabled="False" Padding="16,6" ToolTip="Phase 9 提供"/>
                </StackPanel>
            </StackPanel>
        </Border>

        <!-- 右：模擬預覽（面板局部 Cursor，絕不動全域） -->
        <Border Grid.Column="2" Style="{StaticResource Card}">
            <StackPanel>
                <TextBlock Text="模擬預覽" Style="{StaticResource CardTitle}"/>
                <TextBlock Text="把滑鼠移入下方區域試用游標" Foreground="#6B7280" FontSize="11" Margin="0,0,0,6"/>
                <Border Background="White" BorderBrush="#D1D5DB" BorderThickness="1" Padding="10"
                        Cursor="{Binding PreviewCursor}">
                    <StackPanel>
                        <TextBlock Text="白色背景範例文字" Margin="0,0,0,6"/>
                        <Button Content="範例按鈕" Padding="10,4" HorizontalAlignment="Left" Margin="0,0,0,6"/>
                        <CheckBox Content="範例 Checkbox" Margin="0,0,0,6"/>
                        <TextBlock Margin="0,0,0,6">
                            <Hyperlink>範例連結</Hyperlink>
                        </TextBlock>
                        <TextBox Text="可輸入的文字區域"/>
                    </StackPanel>
                </Border>
                <Border Background="#1F2937" BorderBrush="#D1D5DB" BorderThickness="1" Padding="10" Margin="0,10,0,0"
                        Cursor="{Binding PreviewCursor}">
                    <StackPanel>
                        <TextBlock Text="深色背景範例文字" Foreground="White" Margin="0,0,0,6"/>
                        <Button Content="範例按鈕" Padding="10,4" HorizontalAlignment="Left" Margin="0,0,0,6"/>
                        <CheckBox Content="範例 Checkbox" Foreground="White"/>
                    </StackPanel>
                </Border>
                <TextBlock Margin="0,8,0,0" Foreground="#6B7280">
                    <Run Text="Hotspot："/><Run Text="{Binding HotspotX, Mode=OneWay}"/><Run Text=","/><Run Text="{Binding HotspotY, Mode=OneWay}"/>
                </TextBlock>
            </StackPanel>
        </Border>
    </Grid>
</Window>
```

- [ ] **Step 2: `Views/CursorEditorWindow.xaml.cs`**

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MousePilot.ViewModels;

namespace MousePilot.Views;

public partial class CursorEditorWindow : Window
{
    public CursorEditorWindow(CursorEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Dispose(); // 釋放 VM 快取的原圖 Bitmap
        viewModel.CloseRequested += () =>
        {
            DialogResult = true;
            Close();
        };
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CursorEditorViewModel.HotspotX)
                or nameof(CursorEditorViewModel.HotspotY)
                or nameof(CursorEditorViewModel.SelectedSize)
                or nameof(CursorEditorViewModel.PreviewImage))
            {
                DrawHotspotMarker();
            }
        };
    }

    private CursorEditorViewModel Vm => (CursorEditorViewModel)DataContext;

    private void OnSourceClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: CursorSourceItem item })
        {
            Vm.SelectedSource = item;
        }
    }

    private void OnEditImageClicked(object sender, MouseButtonEventArgs e)
    {
        if (!Vm.CanEditProcessing)
        {
            return; // .cur/.ani 原樣使用，不可改 hotspot
        }

        var pos = e.GetPosition(EditImage);
        var (x, y) = HotspotMath.DisplayToPixel(pos.X, pos.Y, EditImage.ActualWidth, Vm.SelectedSize);
        Vm.SetHotspot(x, y);
    }

    /// <summary>十字準心標記 + 尺寸 ≤ 48 時畫座標格線（規格 §9）。</summary>
    private void DrawHotspotMarker()
    {
        HotspotOverlay.Children.Clear();
        var display = 256.0;
        var size = Vm.SelectedSize;
        if (Vm.PreviewImage is null || size <= 0)
        {
            return;
        }

        if (size <= 48)
        {
            var gridBrush = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0));
            for (var i = 1; i < size; i++)
            {
                var offset = i * display / size;
                HotspotOverlay.Children.Add(new Line { X1 = offset, Y1 = 0, X2 = offset, Y2 = display, Stroke = gridBrush, StrokeThickness = 1 });
                HotspotOverlay.Children.Add(new Line { X1 = 0, Y1 = offset, X2 = display, Y2 = offset, Stroke = gridBrush, StrokeThickness = 1 });
            }
        }

        var cx = HotspotMath.PixelToDisplayCenter(Vm.HotspotX, display, size);
        var cy = HotspotMath.PixelToDisplayCenter(Vm.HotspotY, display, size);
        var cross = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        HotspotOverlay.Children.Add(new Line { X1 = cx - 10, Y1 = cy, X2 = cx + 10, Y2 = cy, Stroke = cross, StrokeThickness = 2 });
        HotspotOverlay.Children.Add(new Line { X1 = cx, Y1 = cy - 10, X2 = cx, Y2 = cy + 10, Stroke = cross, StrokeThickness = 2 });
    }
}
```

- [ ] **Step 3: Build + 測試 + 冒煙**

Run: `dotnet build -c Release`（0 error 0 warning）、`dotnet test tests/MousePilot.Tests`（216 綠）。
啟動冒煙：背景啟動主程式 4 秒存活、Stop-Process（編輯視窗本身留待 Task 4 接上後由使用者實機驗證）。

- [ ] **Step 4: Commit**

```text
feat: CursorEditorWindow - 編輯/預覽視窗版面與 Hotspot 標記

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

### Task 4: 主視窗整合（TDD）

**Files:**
- Modify: `ViewModels/MainViewModel.cs`
- Modify: `Views/MainWindow.xaml`（游標卡加「選擇 / 編輯游標」按鈕）
- Modify: `Views/MainWindow.xaml.cs`（editor launcher 生產實作）
- Test: `tests/MousePilot.Tests/MainViewModelTests.cs`

**Interfaces:**
- Produces: `MainViewModel` 建構子第八參數 `Func<CursorEditorViewModel, bool?>? cursorEditorLauncher = null`（null → 由 View 於 Loaded 時注入 `AttachCursorEditorLauncher`；建構期無副作用，既有建構點不需補參數）；`public void AttachCursorEditorLauncher(Func<CursorEditorViewModel, bool?> launcher)`；`EditCursorCommand`（開編輯器；回傳 true → SaveSettings + 刷新顯示）；`RefreshCursorFileText` 擴充：`CursorPreset` 非空 → 顯示 preset 顯示名稱 + 尺寸。

- [ ] **Step 1: 寫失敗測試（`tests/MousePilot.Tests/MainViewModelTests.cs` 新增）**

```csharp
    [Fact]
    public void 編輯游標確認後刷新顯示()
    {
        var vm = CreateVmWithStartup(new NoOpStartupService());
        vm.AttachCursorEditorLauncher(editorVm =>
        {
            editorVm.SelectedSource = editorVm.Sources.First(s => s.Source.Id == "preset:Heart");
            editorVm.ConfirmCommand.Execute(null);
            return true;
        });

        vm.EditCursorCommand.Execute(null);

        Assert.Equal("Heart", vm.Settings.CursorPreset);
        Assert.Contains("愛心", vm.CursorFileText);
        Assert.Contains("32", vm.CursorFileText);
    }

    [Fact]
    public void 編輯游標取消不改設定()
    {
        var vm = CreateVmWithStartup(new NoOpStartupService());
        vm.AttachCursorEditorLauncher(_ => false);

        vm.EditCursorCommand.Execute(null);

        Assert.Equal("", vm.Settings.CursorPreset);
        Assert.Equal("未選擇", vm.CursorFileText);
    }

    [Fact]
    public void 未接上編輯器時命令不當機()
    {
        var vm = CreateVmWithStartup(new NoOpStartupService());
        var ex = Record.Exception(() => vm.EditCursorCommand.Execute(null));
        Assert.Null(ex);
    }
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 編譯失敗。

- [ ] **Step 3: 實作**

`ViewModels/MainViewModel.cs`：

1. 欄位 `private Func<CursorEditorViewModel, bool?>? _cursorEditorLauncher;`；建構子第八參數 `Func<CursorEditorViewModel, bool?>? cursorEditorLauncher = null`，建構子內 `_cursorEditorLauncher = cursorEditorLauncher;`。
2. 新增：

```csharp
    /// <summary>View 於 Loaded 時注入編輯視窗啟動器（測試直接注入 fake）。</summary>
    public void AttachCursorEditorLauncher(Func<CursorEditorViewModel, bool?> launcher)
        => _cursorEditorLauncher = launcher;

    [RelayCommand]
    private void EditCursor()
    {
        if (_cursorEditorLauncher is null)
        {
            return; // 尚未接上 View（防呆，不當機）
        }

        var editorVm = new CursorEditorViewModel(Settings);
        if (_cursorEditorLauncher(editorVm) == true)
        {
            SaveSettings();
            _lastImportSize = (null, null);
            RefreshCursorFileText();
            RemoveCursorCommand.NotifyCanExecuteChanged();
        }
    }
```

3. `RefreshCursorFileText()` 改為：

```csharp
    private void RefreshCursorFileText()
    {
        if (Settings.CursorPreset.Length > 0)
        {
            var preset = CursorGallery.Presets.FirstOrDefault(p => p.Id == Settings.CursorPreset);
            CursorFileText = $"{preset?.DisplayName ?? Settings.CursorPreset}（{Settings.CursorSize} px）";
            return;
        }

        if (Settings.CursorFile.Length == 0)
        {
            CursorFileText = "未選擇";
            return;
        }

        var name = System.IO.Path.GetFileName(Settings.CursorFile);
        CursorFileText = _lastImportSize.Width is { } w && _lastImportSize.Height is { } h
            ? $"{name}（{w}x{h}）"
            : name;
    }
```

4. `Views/MainWindow.xaml` 游標卡按鈕列最前面加：

```xml
                        <Button Content="選擇 / 編輯游標" Command="{Binding EditCursorCommand}" Padding="12,6" Margin="0,0,8,0"/>
```

5. `Views/MainWindow.xaml.cs` 建構子內（`InitializeComponent();` 之後）加：

```csharp
        Loaded += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.AttachCursorEditorLauncher(editorVm =>
                    new CursorEditorWindow(editorVm) { Owner = this }.ShowDialog());
            }
        };
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`（216 + 3 = 219 全綠）、`dotnet build -c Release`（0 warning）。
啟動冒煙：背景啟動 4 秒存活、Stop-Process。

- [ ] **Step 5: Commit**

```text
feat: 主視窗接上游標編輯器 - 選擇/確認/顯示刷新

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

### Task 5: Phase 收尾

**Files:**
- Modify: `CHANGELOG.md`
- Modify: `docs/superpowers/plans/2026-08-20-mousepilot-master-plan.md`（Phase 8 → ✅ 完成、細部計畫文件欄填 `2026-08-23-phase8-hotspot-editor.md`）

- [ ] **Step 1: CHANGELOG [Unreleased]「### 新增」補上**

```markdown
- 游標編輯器（Phase 8）：預設圖案 Grid（16 內建 + 匯入檔案）與我的收藏、尺寸選擇、點擊設 Hotspot（十字標記/座標格/手動輸入）、JPG 去背（左上角參考色 + 容差）、雙背景模擬預覽（面板局部游標，不動全域）；移除失敗防孤兒檔案。
```

- [ ] **Step 2: Master Plan 更新（僅 Phase 8 列與細部計畫文件欄）**

- [ ] **Step 3: 最終驗證**

```powershell
dotnet build -c Release
dotnet test tests/MousePilot.Tests
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

- [ ] **Step 4: Commit**

```text
docs: 更新 CHANGELOG 與進度總表 - Phase 8 完成

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

## Phase 8 完成定義

- [ ] build 0 error、測試全綠（預期 219）、publish 成功。
- [ ] 單元測試涵蓋：來源清單/預設值/夾制/管線（含左上角參考色去背與退化警告）/cur bytes hotspot/收藏/確認互斥寫入/取消；座標換算；移除失敗防孤兒；損毀 cur 匯入。
- [ ] **使用者實機手動驗證（規格 §34 案例 18/19 + 補三~補八）：**
  1. 「選擇 / 編輯游標」開啟編輯視窗：左側 16 圖案 Grid + 已匯入檔案。
  2. 點「愛心」→ 中間顯示放大圖 + 座標格線 + 中心十字；點圖片任一處 → 十字移動、Hotspot 座標更新（案例 18）；手動輸入 X/Y 也生效。
  3. 尺寸下拉換 48 → 圖與格線重繪、hotspot 夾制。
  4. 右側預覽面板：滑鼠移入 → **游標變成自訂圖案**（含 hotspot 位置正確——點按鈕的感覺對）；白/深色背景、按鈕/Checkbox/連結/文字框齊備（案例 19）；期間 Windows 全域游標不變。
  5. 匯入一張 JPG → 勾「移除背景」→ 背景消失；容差調 0 與 100 看差異；全背景圖 → 顯示紅字警告。
  6. 加入收藏 → 左上收藏區出現；重開程式仍在（settings.json）。
  7. 「確定」→ 主視窗游標卡顯示選擇（名稱 + 尺寸）；重開程式保留。
