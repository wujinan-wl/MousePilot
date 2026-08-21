# MousePilot Phase 7：Cursor Import（含內建圖案庫）實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 自訂游標的「圖片處理半邊」：匯入 PNG/JPG/JPEG/BMP/CUR/ANI、透明裁切、等比縮放（16~128）、JPG 簡易去背、.cur 檔讀寫（含 hotspot）、ANI 首格預覽解析（失敗優雅降級）、16 個程式繪製的內建圖案、匯入檔落地 `%AppData%\MousePilot\Cursors\`、收藏資料層；Dashboard 游標卡啟用匯入/移除。

**Architecture:** 四層純度遞減：`CursorImageProcessor`（純圖像運算：裁切/去背/縮放，LockBits 實作）→ `CurFileFormat`（.cur 位元組寫/讀 + ANI 首格，全部 Try 語意）→ `CursorGallery`（16 內建圖案**程式繪製**——無二進位資產，天然滿足「Embed 進 assembly、單一 EXE」）→ `CursorImportService`（檔案落地與探測，storage dir 可注入）。全部用 System.Drawing（UseWindowsForms 已引入，無新相依）。**不在範圍**：套用全域游標（Phase 9）、Hotspot 編輯與預設圖案 Grid/預覽面板/收藏 UI（Phase 8——本階段只做收藏資料層）。

**Tech Stack:** 既有。無新相依。

**Spec:** `docs/spec/mousepilot-spec.md`（§7/§8、§21、補充需求 補一~補九、§34 案例 13~17）；Master Plan Phase 7 章節。

## 計畫決策（供使用者知悉，可否決）

- **內建圖案採程式繪製**（GDI+ 向量風格）而非嵌入圖片檔：無授權疑慮、無資產管線、單一 EXE 天然成立、任意尺寸清晰；代價是圖案風格簡約（v1 可接受，未來可換嵌入資產）。「藍色機器貓風格」為自繪 generic 造型，id 採規格範例的 `CuteRobotCat`；**不包含任何哆啦A夢素材**（規格補二）。
- **.cur 寫入採 32bpp DIB**（非 PNG 壓縮條目）：所有 Windows 版本通用；ANI 不自行產生、只解析首格供預覽（套用時 Phase 9 用 `LoadCursorFromFile`，Windows 原生支援 .ani）。

## Global Constraints

- 支援副檔名：`.png .jpg .jpeg .bmp .cur .ani`（規格 §7）；尺寸集 {16,24,32,48,64,96,128}（既有 `AppSettings.AllowedCursorSizes`）；等比縮放置中、不拉伸（規格 §8）。
- 去背（規格補六）：參考色 + 容差（Chebyshev：RGB 各通道差的最大值 ≤ tolerance → 透明）；預設參考色 = 左上角像素。
- 錯誤不 crash（規格 §21）：檔案不存在/圖片損毀/CUR/ANI 解析失敗一律 Try 語意或 Result 物件，UI 顯示 Notice。
- 匯入檔複製到 `%AppData%\MousePilot\Cursors\`（同名自動編號）；移除只允許刪 storage dir 內的檔案。
- 收藏（規格補八）：`AppSettings.FavoriteCursors : List<string>`（camelCase `favoriteCursors`），id 格式 `preset:<Id>` / `file:<檔名>`；本階段僅資料層。
- 內建 16 圖案（規格補一）：基本 8（Arrow/Dot/Crosshair/Hand/Heart/Star/Lightning/Flame，DefaultSize 32）+ 可愛 8（Cat/Dog/Bear/Rabbit/Frog/Ghost/CuteRobotCat/Robot，DefaultSize 48，規格補五）；預設 hotspot：Arrow/Hand=左上、其餘=中心（規格補七）。
- 測試不依賴真實 `%AppData%`（storage dir 注入 temp 目錄並清理）；所有 Bitmap 用 `using` 釋放。
- **（先例硬性）**測試檔內每個 `new MainViewModel(` 建構點維持全服務注入；本階段新增的 VM 參數（cursorImportService/cursorFilePicker）建構期無副作用，既有建構點**不需**補參數（新測試才注入）。
- TDD；目前基準 152 綠。Commit 一律 `git commit -F <$env:TEMP 暫存檔>`（禁 here-string），繁中+前綴+`Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`，commit 後 `git log -1 --format=%B` 驗證。

---

### Task 1: CursorImageProcessor（TDD）

**Files:**
- Create: `Services/CursorImageProcessor.cs`
- Test: `tests/MousePilot.Tests/CursorImageProcessorTests.cs`

**Interfaces:**
- Produces（Task 4/5 依賴，逐字）: `static class CursorImageProcessor`：`static Bitmap TrimTransparent(Bitmap source)`（全透明→1×1）、`static Bitmap RemoveBackground(Bitmap source, Color reference, int tolerance)`、`static Bitmap ScaleProportional(Bitmap source, int targetSize)`（置中、透明底、比例保持）。回傳皆為新 Bitmap（Format32bppArgb），呼叫端 Dispose。

- [ ] **Step 1: 寫失敗測試**

`tests/MousePilot.Tests/CursorImageProcessorTests.cs`：

```csharp
using System.Drawing;
using MousePilot.Services;

namespace MousePilot.Tests;

public class CursorImageProcessorTests
{
    private static Bitmap MakeBitmap(int w, int h, Action<Bitmap>? paint = null)
    {
        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        paint?.Invoke(bmp);
        return bmp;
    }

    [Fact]
    public void 裁切至非透明外接矩形()
    {
        using var src = MakeBitmap(10, 10, b =>
        {
            b.SetPixel(3, 4, Color.Red);
            b.SetPixel(6, 7, Color.Blue);
        });
        using var trimmed = CursorImageProcessor.TrimTransparent(src);
        Assert.Equal(4, trimmed.Width);   // x: 3..6
        Assert.Equal(4, trimmed.Height);  // y: 4..7
        Assert.Equal(Color.Red.ToArgb(), trimmed.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.Blue.ToArgb(), trimmed.GetPixel(3, 3).ToArgb());
    }

    [Fact]
    public void 全透明圖回傳1x1()
    {
        using var src = MakeBitmap(8, 8);
        using var trimmed = CursorImageProcessor.TrimTransparent(src);
        Assert.Equal(1, trimmed.Width);
        Assert.Equal(1, trimmed.Height);
    }

    [Fact]
    public void 去背以容差判定並保留其他像素()
    {
        using var src = MakeBitmap(3, 1, b =>
        {
            b.SetPixel(0, 0, Color.FromArgb(255, 250, 250, 250)); // 接近白（容差內）
            b.SetPixel(1, 0, Color.FromArgb(255, 200, 200, 200)); // 容差外
            b.SetPixel(2, 0, Color.FromArgb(255, 255, 0, 0));     // 紅
        });
        using var result = CursorImageProcessor.RemoveBackground(src, Color.White, 10);
        Assert.Equal(0, result.GetPixel(0, 0).A);
        Assert.Equal(255, result.GetPixel(1, 0).A);
        Assert.Equal(Color.FromArgb(255, 255, 0, 0).ToArgb(), result.GetPixel(2, 0).ToArgb());
    }

    [Fact]
    public void 去背容差為Chebyshev距離()
    {
        using var src = MakeBitmap(2, 1, b =>
        {
            b.SetPixel(0, 0, Color.FromArgb(255, 245, 255, 255)); // maxΔ=10 → 透明
            b.SetPixel(1, 0, Color.FromArgb(255, 244, 255, 255)); // maxΔ=11 → 保留
        });
        using var result = CursorImageProcessor.RemoveBackground(src, Color.White, 10);
        Assert.Equal(0, result.GetPixel(0, 0).A);
        Assert.Equal(255, result.GetPixel(1, 0).A);
    }

    [Fact]
    public void 等比縮放寬圖置中不拉伸()
    {
        using var src = MakeBitmap(64, 32, b =>
        {
            using var g = Graphics.FromImage(b);
            g.Clear(Color.Lime);
        });
        using var scaled = CursorImageProcessor.ScaleProportional(src, 32);
        Assert.Equal(32, scaled.Width);
        Assert.Equal(32, scaled.Height);
        Assert.Equal(0, scaled.GetPixel(16, 2).A);    // 上邊帶透明（比例 2:1 → 高 16，置中 y=8..23）
        Assert.NotEqual(0, scaled.GetPixel(16, 16).A); // 中心有內容
        Assert.Equal(0, scaled.GetPixel(16, 29).A);    // 下邊帶透明
    }

    [Fact]
    public void 等比縮放高圖置中()
    {
        using var src = MakeBitmap(16, 64, b =>
        {
            using var g = Graphics.FromImage(b);
            g.Clear(Color.Red);
        });
        using var scaled = CursorImageProcessor.ScaleProportional(src, 64);
        Assert.Equal(64, scaled.Width);
        Assert.Equal(0, scaled.GetPixel(2, 32).A);     // 左邊帶透明（寬 16，置中 x=24..39）
        Assert.NotEqual(0, scaled.GetPixel(32, 32).A);
    }

    [Fact]
    public void 縮放結果為32bppArgb()
    {
        using var src = MakeBitmap(10, 10);
        using var scaled = CursorImageProcessor.ScaleProportional(src, 16);
        Assert.Equal(System.Drawing.Imaging.PixelFormat.Format32bppArgb, scaled.PixelFormat);
    }
}
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 編譯失敗（`CursorImageProcessor` 不存在）。

- [ ] **Step 3: 實作 Services/CursorImageProcessor.cs**

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MousePilot.Services;

/// <summary>游標圖片處理（純運算；LockBits 存取避免 GetPixel 逐點的效能問題）。回傳皆為新 Bitmap，呼叫端負責 Dispose。</summary>
public static class CursorImageProcessor
{
    /// <summary>裁切至非透明內容的外接矩形（規格補六「自動裁切透明區域」）；全透明回傳 1×1 透明圖。</summary>
    public static Bitmap TrimTransparent(Bitmap source)
    {
        using var normalized = ToArgb(source);
        var bytes = ReadArgb(normalized, out var stride);
        int minX = normalized.Width, minY = normalized.Height, maxX = -1, maxY = -1;
        for (var y = 0; y < normalized.Height; y++)
        {
            for (var x = 0; x < normalized.Width; x++)
            {
                if (bytes[(y * stride) + (x * 4) + 3] > 0)
                {
                    if (x < minX) { minX = x; }
                    if (x > maxX) { maxX = x; }
                    if (y < minY) { minY = y; }
                    if (y > maxY) { maxY = y; }
                }
            }
        }

        if (maxX < 0)
        {
            return new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        }

        return normalized.Clone(
            Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1), PixelFormat.Format32bppArgb);
    }

    /// <summary>把與參考色 Chebyshev 距離 ≤ tolerance 的像素設為透明（規格補六 JPG 簡易去背）。</summary>
    public static Bitmap RemoveBackground(Bitmap source, Color reference, int tolerance)
    {
        var result = ToArgb(source);
        var bytes = ReadArgb(result, out var stride);
        for (var y = 0; y < result.Height; y++)
        {
            for (var x = 0; x < result.Width; x++)
            {
                var i = (y * stride) + (x * 4);
                var maxDelta = Math.Max(
                    Math.Abs(bytes[i + 2] - reference.R),
                    Math.Max(Math.Abs(bytes[i + 1] - reference.G), Math.Abs(bytes[i] - reference.B)));
                if (maxDelta <= tolerance)
                {
                    bytes[i + 3] = 0;
                }
            }
        }

        WriteArgb(result, bytes);
        return result;
    }

    /// <summary>等比縮放進 targetSize×targetSize 透明畫布並置中（規格 §8：保持比例、不強制拉伸）。</summary>
    public static Bitmap ScaleProportional(Bitmap source, int targetSize)
    {
        var result = new Bitmap(targetSize, targetSize, PixelFormat.Format32bppArgb);
        var scale = Math.Min((double)targetSize / source.Width, (double)targetSize / source.Height);
        var w = Math.Max(1, (int)Math.Round(source.Width * scale));
        var h = Math.Max(1, (int)Math.Round(source.Height * scale));
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);
        g.DrawImage(source, (targetSize - w) / 2, (targetSize - h) / 2, w, h);
        return result;
    }

    private static Bitmap ToArgb(Bitmap source)
    {
        var clone = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(clone);
        g.DrawImageUnscaled(source, 0, 0);
        return clone;
    }

    private static byte[] ReadArgb(Bitmap bmp, out int stride)
    {
        var data = bmp.LockBits(
            new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            stride = data.Stride;
            var bytes = new byte[data.Stride * bmp.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private static void WriteArgb(Bitmap bmp, byte[] bytes)
    {
        var data = bmp.LockBits(
            new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 全綠（152 + 7 = 159）。

- [ ] **Step 5: Commit**

```text
feat: CursorImageProcessor - 透明裁切/去背/等比縮放

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

### Task 2: CurFileFormat（TDD）

**Files:**
- Create: `Services/CurFileFormat.cs`
- Test: `tests/MousePilot.Tests/CurFileFormatTests.cs`

**Interfaces:**
- Produces（Task 4 依賴，逐字）:
  - `readonly record struct CurImage(int Width, int Height, int HotspotX, int HotspotY)`
  - `static class CurFileFormat`：`static byte[] Write(Bitmap image, int hotspotX, int hotspotY)`（單條目 32bpp DIB .cur）；`static (CurImage Info, Bitmap Image)? TryReadFirstImage(byte[] data)`（.cur/.ico 首條目；PNG 條目與 32bpp DIB 皆支援；任何異常→null）；`static (CurImage Info, Bitmap Image)? TryReadAniFirstFrame(byte[] data)`（RIFF/ACON 首個 icon chunk；任何異常→null）。

- [ ] **Step 1: 寫失敗測試**

`tests/MousePilot.Tests/CurFileFormatTests.cs`：

```csharp
using System.Drawing;
using MousePilot.Services;

namespace MousePilot.Tests;

public class CurFileFormatTests
{
    private static Bitmap MakeTestImage()
    {
        var bmp = new Bitmap(8, 8, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        bmp.SetPixel(0, 0, Color.FromArgb(255, 255, 0, 0));   // 左上紅
        bmp.SetPixel(7, 7, Color.FromArgb(255, 0, 0, 255));   // 右下藍
        bmp.SetPixel(3, 3, Color.FromArgb(128, 0, 255, 0));   // 半透明綠
        return bmp;
    }

    [Fact]
    public void 寫入後可讀回尺寸與Hotspot()
    {
        using var img = MakeTestImage();
        var bytes = CurFileFormat.Write(img, 2, 5);

        var read = CurFileFormat.TryReadFirstImage(bytes);

        Assert.NotNull(read);
        Assert.Equal(new CurImage(8, 8, 2, 5), read!.Value.Info);
        read.Value.Image.Dispose();
    }

    [Fact]
    public void 寫入後像素往返一致()
    {
        using var img = MakeTestImage();
        var bytes = CurFileFormat.Write(img, 0, 0);

        var read = CurFileFormat.TryReadFirstImage(bytes);
        using var round = read!.Value.Image;

        Assert.Equal(Color.FromArgb(255, 255, 0, 0).ToArgb(), round.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.FromArgb(255, 0, 0, 255).ToArgb(), round.GetPixel(7, 7).ToArgb());
        Assert.Equal(Color.FromArgb(128, 0, 255, 0).ToArgb(), round.GetPixel(3, 3).ToArgb());
        Assert.Equal(0, round.GetPixel(5, 5).A); // 未設定像素維持透明
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3, 4 })]
    public void 損毀資料回傳null(byte[] data)
    {
        Assert.Null(CurFileFormat.TryReadFirstImage(data));
        Assert.Null(CurFileFormat.TryReadAniFirstFrame(data));
    }

    [Fact]
    public void 截斷的cur回傳null()
    {
        using var img = MakeTestImage();
        var bytes = CurFileFormat.Write(img, 0, 0);
        Assert.Null(CurFileFormat.TryReadFirstImage(bytes[..30]));
    }

    [Fact]
    public void ANI首格可解析()
    {
        using var img = MakeTestImage();
        var cur = CurFileFormat.Write(img, 1, 2);
        var ani = BuildAni(cur);

        var read = CurFileFormat.TryReadAniFirstFrame(ani);

        Assert.NotNull(read);
        Assert.Equal(new CurImage(8, 8, 1, 2), read!.Value.Info);
        read.Value.Image.Dispose();
    }

    [Fact]
    public void 非ACON的RIFF回傳null()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(4);
        w.Write("WAVE"u8);
        Assert.Null(CurFileFormat.TryReadAniFirstFrame(ms.ToArray()));
    }

    private static byte[] BuildAni(byte[] cur)
    {
        var pad = cur.Length % 2;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var iconChunkTotal = 8 + cur.Length + pad;  // 'icon' + size + data + padding
        var listSize = 4 + iconChunkTotal;          // 'fram' + icon chunk
        var riffSize = 4 + 8 + listSize;            // 'ACON' + LIST header + LIST content
        w.Write("RIFF"u8);
        w.Write(riffSize);
        w.Write("ACON"u8);
        w.Write("LIST"u8);
        w.Write(listSize);
        w.Write("fram"u8);
        w.Write("icon"u8);
        w.Write(cur.Length);
        w.Write(cur);
        if (pad == 1)
        {
            w.Write((byte)0);
        }

        return ms.ToArray();
    }
}
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 編譯失敗。

- [ ] **Step 3: 實作 Services/CurFileFormat.cs**

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace MousePilot.Services;

public readonly record struct CurImage(int Width, int Height, int HotspotX, int HotspotY);

/// <summary>
/// .cur 檔讀寫（規格 §8/§9：hotspot 存於 ICONDIRENTRY）與 ANI 首格解析（規格 §21：失敗優雅降級 → null）。
/// 寫入採 32bpp DIB 條目（全 Windows 版本通用）；讀取支援 DIB 與 PNG 壓縮條目。
/// </summary>
public static class CurFileFormat
{
    /// <summary>把 32bppArgb 圖寫成單條目 .cur。寬高上限 256（.cur 格式限制，寫入前應先縮放）。</summary>
    public static byte[] Write(Bitmap image, int hotspotX, int hotspotY)
    {
        var w = image.Width;
        var h = image.Height;
        var xorStride = w * 4;
        var andStride = ((w + 31) / 32) * 4; // 1bpp，每列補齊 32 位元
        var imageSize = 40 + (xorStride * h) + (andStride * h);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((ushort)0);              // reserved
        bw.Write((ushort)2);              // type 2 = cursor
        bw.Write((ushort)1);              // count
        bw.Write((byte)(w == 256 ? 0 : w));
        bw.Write((byte)(h == 256 ? 0 : h));
        bw.Write((byte)0);                // color count
        bw.Write((byte)0);                // reserved
        bw.Write((ushort)hotspotX);
        bw.Write((ushort)hotspotY);
        bw.Write((uint)imageSize);
        bw.Write((uint)22);               // offset = 6 + 16

        bw.Write(40);                     // BITMAPINFOHEADER
        bw.Write(w);
        bw.Write(h * 2);                  // XOR + AND
        bw.Write((ushort)1);
        bw.Write((ushort)32);
        bw.Write(0);                      // BI_RGB
        bw.Write((xorStride * h) + (andStride * h));
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);

        var pixels = ReadArgb(image);
        for (var y = h - 1; y >= 0; y--) // DIB 由下而上
        {
            bw.Write(pixels, y * xorStride, xorStride);
        }

        var andRow = new byte[andStride]; // alpha 承擔透明，AND mask 全 0
        for (var y = 0; y < h; y++)
        {
            bw.Write(andRow);
        }

        return ms.ToArray();
    }

    /// <summary>讀取 .cur/.ico 首條目；任何格式異常回傳 null（規格 §21）。</summary>
    public static (CurImage Info, Bitmap Image)? TryReadFirstImage(byte[] data)
    {
        try
        {
            if (data.Length < 22)
            {
                return null;
            }

            var type = BitConverter.ToUInt16(data, 2);
            var count = BitConverter.ToUInt16(data, 4);
            if (BitConverter.ToUInt16(data, 0) != 0 || (type != 1 && type != 2) || count < 1)
            {
                return null;
            }

            int width = data[6] == 0 ? 256 : data[6];
            int height = data[7] == 0 ? 256 : data[7];
            var hotspotX = type == 2 ? BitConverter.ToUInt16(data, 10) : 0;
            var hotspotY = type == 2 ? BitConverter.ToUInt16(data, 12) : 0;
            var size = BitConverter.ToInt32(data, 14);
            var offset = BitConverter.ToInt32(data, 18);
            if (offset < 22 || size < 4 || offset + size > data.Length)
            {
                return null;
            }

            Bitmap image;
            if (data[offset] == 0x89 && data[offset + 1] == 0x50) // PNG 條目
            {
                using var pngStream = new MemoryStream(data, offset, size);
                image = new Bitmap(pngStream);
            }
            else
            {
                var dib = TryDecodeDib32(data, offset, size, width, height);
                if (dib is null)
                {
                    return null;
                }

                image = dib;
            }

            return (new CurImage(image.Width, image.Height, hotspotX, hotspotY), image);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>解析 ANI（RIFF/ACON）的第一個 icon chunk；任何異常回傳 null。</summary>
    public static (CurImage Info, Bitmap Image)? TryReadAniFirstFrame(byte[] data)
    {
        try
        {
            if (data.Length < 12
                || data[0] != 'R' || data[1] != 'I' || data[2] != 'F' || data[3] != 'F'
                || data[8] != 'A' || data[9] != 'C' || data[10] != 'O' || data[11] != 'N')
            {
                return null;
            }

            return FindIconChunk(data, 12, data.Length);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static (CurImage Info, Bitmap Image)? FindIconChunk(byte[] data, int start, int end)
    {
        var pos = start;
        while (pos + 8 <= end)
        {
            var id = System.Text.Encoding.ASCII.GetString(data, pos, 4);
            var size = BitConverter.ToInt32(data, pos + 4);
            if (size < 0 || pos + 8 + size > data.Length)
            {
                return null;
            }

            if (id == "icon")
            {
                return TryReadFirstImage(data[(pos + 8)..(pos + 8 + size)]);
            }

            if (id == "LIST" && size >= 4)
            {
                var inner = FindIconChunk(data, pos + 12, pos + 8 + size);
                if (inner is not null)
                {
                    return inner;
                }
            }

            pos += 8 + size + (size % 2); // chunk 補齊偶數
        }

        return null;
    }

    private static Bitmap? TryDecodeDib32(byte[] data, int offset, int size, int width, int height)
    {
        if (size < 40 || BitConverter.ToInt32(data, offset) != 40)
        {
            return null;
        }

        var bitCount = BitConverter.ToUInt16(data, offset + 14);
        if (bitCount != 32)
        {
            return null; // 只支援 32bpp（自家 Write 的格式；其他 bpp 屬罕見舊格式 → 優雅降級）
        }

        var xorStride = width * 4;
        if (offset + 40 + (xorStride * height) > data.Length)
        {
            return null;
        }

        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var pixels = new byte[xorStride * height];
        for (var y = 0; y < height; y++)
        {
            Array.Copy(data, offset + 40 + ((height - 1 - y) * xorStride), pixels, y * xorStride, xorStride);
        }

        WriteArgb(bmp, pixels);
        return bmp;
    }

    private static byte[] ReadArgb(Bitmap bmp)
    {
        var data = bmp.LockBits(
            new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[bmp.Width * 4 * bmp.Height];
            for (var y = 0; y < bmp.Height; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), bytes, y * bmp.Width * 4, bmp.Width * 4);
            }

            return bytes;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private static void WriteArgb(Bitmap bmp, byte[] bytes)
    {
        var data = bmp.LockBits(
            new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < bmp.Height; y++)
            {
                Marshal.Copy(bytes, y * bmp.Width * 4, data.Scan0 + (y * data.Stride), bmp.Width * 4);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 全綠（159 + 7 = 166）。

- [ ] **Step 5: Commit**

```text
feat: CurFileFormat - cur 讀寫與 ANI 首格解析

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

### Task 3: CursorGallery（16 內建圖案，TDD）

**Files:**
- Create: `Services/CursorGallery.cs`
- Test: `tests/MousePilot.Tests/CursorGalleryTests.cs`

**Interfaces:**
- Produces（Task 5/Phase 8 依賴，逐字）:
  - `enum CursorCategory { Basic, Cute }`
  - `sealed record CursorPreset(string Id, string DisplayName, CursorCategory Category, bool HotspotTopLeft, int DefaultSize)`
  - `static class CursorGallery`：`static IReadOnlyList<CursorPreset> Presets`（16 項，順序：基本 8 → 可愛 8）；`static Bitmap Render(string id, int size)`（未知 id 擲 `ArgumentException`）。

- [ ] **Step 1: 寫失敗測試**

`tests/MousePilot.Tests/CursorGalleryTests.cs`：

```csharp
using MousePilot.Services;

namespace MousePilot.Tests;

public class CursorGalleryTests
{
    [Fact]
    public void 共16個圖案且Id唯一()
    {
        Assert.Equal(16, CursorGallery.Presets.Count);
        Assert.Equal(16, CursorGallery.Presets.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void 基本與可愛各8個()
    {
        Assert.Equal(8, CursorGallery.Presets.Count(p => p.Category == CursorCategory.Basic));
        Assert.Equal(8, CursorGallery.Presets.Count(p => p.Category == CursorCategory.Cute));
    }

    [Fact]
    public void 預設尺寸_基本32可愛48()
    {
        Assert.All(CursorGallery.Presets.Where(p => p.Category == CursorCategory.Basic), p => Assert.Equal(32, p.DefaultSize));
        Assert.All(CursorGallery.Presets.Where(p => p.Category == CursorCategory.Cute), p => Assert.Equal(48, p.DefaultSize));
    }

    [Fact]
    public void 預設Hotspot_僅箭頭與手指為左上()
    {
        Assert.Equal(
            new[] { "Arrow", "Hand" },
            CursorGallery.Presets.Where(p => p.HotspotTopLeft).Select(p => p.Id).ToArray());
    }

    [Fact]
    public void 包含規格範例Id_CuteRobotCat()
    {
        Assert.Contains(CursorGallery.Presets, p => p.Id == "CuteRobotCat");
    }

    [Theory]
    [InlineData(16)]
    [InlineData(48)]
    public void 全部圖案可繪製且非全透明(int size)
    {
        foreach (var preset in CursorGallery.Presets)
        {
            using var bmp = CursorGallery.Render(preset.Id, size);
            Assert.Equal(size, bmp.Width);
            Assert.Equal(size, bmp.Height);
            var hasContent = false;
            for (var y = 0; y < size && !hasContent; y++)
            {
                for (var x = 0; x < size && !hasContent; x++)
                {
                    hasContent = bmp.GetPixel(x, y).A > 0;
                }
            }

            Assert.True(hasContent, $"{preset.Id} 繪製結果全透明");
        }
    }

    [Fact]
    public void 未知Id擲ArgumentException()
    {
        Assert.Throws<ArgumentException>(() => CursorGallery.Render("Nope", 32));
    }
}
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 編譯失敗。

- [ ] **Step 3: 實作 Services/CursorGallery.cs**

（繪製皆在 32×32 邏輯座標、依 size 縮放；風格：簡約扁平 + 深灰描邊。實作照下方逐字。）

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MousePilot.Services;

public enum CursorCategory
{
    Basic,
    Cute,
}

public sealed record CursorPreset(string Id, string DisplayName, CursorCategory Category, bool HotspotTopLeft, int DefaultSize);

/// <summary>
/// 內建游標圖案庫（規格補一/補九）：全部程式繪製、無二進位資產——單一 EXE 天然成立、無授權疑慮。
/// 「CuteRobotCat」為自繪藍色機器貓風格 generic 造型（規格補二：不包含哆啦A夢素材）。
/// </summary>
public static class CursorGallery
{
    public static IReadOnlyList<CursorPreset> Presets { get; } = new[]
    {
        new CursorPreset("Arrow", "標準箭頭", CursorCategory.Basic, HotspotTopLeft: true, DefaultSize: 32),
        new CursorPreset("Dot", "圓點", CursorCategory.Basic, false, 32),
        new CursorPreset("Crosshair", "十字準心", CursorCategory.Basic, false, 32),
        new CursorPreset("Hand", "手指", CursorCategory.Basic, true, 32),
        new CursorPreset("Heart", "愛心", CursorCategory.Basic, false, 32),
        new CursorPreset("Star", "星星", CursorCategory.Basic, false, 32),
        new CursorPreset("Lightning", "閃電", CursorCategory.Basic, false, 32),
        new CursorPreset("Flame", "火焰", CursorCategory.Basic, false, 32),
        new CursorPreset("Cat", "貓咪", CursorCategory.Cute, false, 48),
        new CursorPreset("Dog", "狗狗", CursorCategory.Cute, false, 48),
        new CursorPreset("Bear", "熊熊", CursorCategory.Cute, false, 48),
        new CursorPreset("Rabbit", "兔子", CursorCategory.Cute, false, 48),
        new CursorPreset("Frog", "青蛙", CursorCategory.Cute, false, 48),
        new CursorPreset("Ghost", "小幽靈", CursorCategory.Cute, false, 48),
        new CursorPreset("CuteRobotCat", "藍色機器貓", CursorCategory.Cute, false, 48),
        new CursorPreset("Robot", "卡通機器人", CursorCategory.Cute, false, 48),
    };

    private static readonly Color Outline = Color.FromArgb(45, 45, 48);

    public static Bitmap Render(string id, int size)
    {
        Action<Graphics> painter = id switch
        {
            "Arrow" => DrawArrow,
            "Dot" => DrawDot,
            "Crosshair" => DrawCrosshair,
            "Hand" => DrawHand,
            "Heart" => DrawHeart,
            "Star" => DrawStar,
            "Lightning" => DrawLightning,
            "Flame" => DrawFlame,
            "Cat" => DrawCat,
            "Dog" => DrawDog,
            "Bear" => DrawBear,
            "Rabbit" => DrawRabbit,
            "Frog" => DrawFrog,
            "Ghost" => DrawGhost,
            "CuteRobotCat" => DrawRobotCat,
            "Robot" => DrawRobot,
            _ => throw new ArgumentException($"未知的內建圖案 id：{id}", nameof(id)),
        };

        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        g.ScaleTransform(size / 32f, size / 32f);
        painter(g);
        return bmp;
    }

    private static Pen OutlinePen() => new(Outline, 1.6f) { LineJoin = LineJoin.Round };

    private static void FillAndOutline(Graphics g, Brush fill, GraphicsPath path)
    {
        g.FillPath(fill, path);
        using var pen = OutlinePen();
        g.DrawPath(pen, path);
    }

    private static void DrawArrow(Graphics g)
    {
        using var path = new GraphicsPath();
        path.AddPolygon(new PointF[]
        {
            new(2, 1), new(2, 25), new(9, 19), new(13, 29), new(17, 27), new(13, 18), new(22, 17),
        });
        path.CloseFigure();
        using var fill = new SolidBrush(Color.White);
        FillAndOutline(g, fill, path);
    }

    private static void DrawDot(Graphics g)
    {
        using var outer = new SolidBrush(Color.White);
        g.FillEllipse(outer, 8, 8, 16, 16);
        using var inner = new SolidBrush(Outline);
        g.FillEllipse(inner, 11, 11, 10, 10);
    }

    private static void DrawCrosshair(Graphics g)
    {
        using var pen = new Pen(Outline, 2.2f);
        g.DrawLine(pen, 16, 2, 16, 12);
        g.DrawLine(pen, 16, 20, 16, 30);
        g.DrawLine(pen, 2, 16, 12, 16);
        g.DrawLine(pen, 20, 16, 30, 16);
        using var dot = new SolidBrush(Color.FromArgb(220, 38, 38));
        g.FillEllipse(dot, 14, 14, 4, 4);
    }

    private static void DrawHand(Graphics g)
    {
        using var path = new GraphicsPath();
        path.AddRectangle(new RectangleF(13, 2, 5, 14));       // 食指
        path.AddEllipse(8, 11, 17, 16);                        // 手掌
        using var fill = new SolidBrush(Color.FromArgb(255, 224, 189));
        FillAndOutline(g, fill, path);
    }

    private static void DrawHeart(Graphics g)
    {
        using var path = new GraphicsPath();
        path.AddArc(5, 5, 12, 12, 135, 225);
        path.AddArc(15, 5, 12, 12, 180, 225);
        path.AddLine(26, 13, 16, 28);
        path.AddLine(16, 28, 6, 13);
        path.CloseFigure();
        using var fill = new SolidBrush(Color.FromArgb(239, 68, 68));
        FillAndOutline(g, fill, path);
    }

    private static void DrawStar(Graphics g)
    {
        var points = new PointF[10];
        for (var i = 0; i < 10; i++)
        {
            var angle = (Math.PI / 5 * i) - (Math.PI / 2);
            var radius = i % 2 == 0 ? 14.0 : 5.6;
            points[i] = new PointF(16 + (float)(radius * Math.Cos(angle)), 16 + (float)(radius * Math.Sin(angle)));
        }

        using var path = new GraphicsPath();
        path.AddPolygon(points);
        using var fill = new SolidBrush(Color.FromArgb(250, 204, 21));
        FillAndOutline(g, fill, path);
    }

    private static void DrawLightning(Graphics g)
    {
        using var path = new GraphicsPath();
        path.AddPolygon(new PointF[]
        {
            new(19, 1), new(7, 18), new(14, 18), new(11, 31), new(25, 12), new(17, 12),
        });
        using var fill = new SolidBrush(Color.FromArgb(250, 204, 21));
        FillAndOutline(g, fill, path);
    }

    private static void DrawFlame(Graphics g)
    {
        using var outer = new GraphicsPath();
        outer.AddBezier(16, 2, 8, 10, 6, 16, 8, 22);
        outer.AddArc(8, 16, 16, 14, 180, -180);
        outer.AddBezier(24, 22, 26, 14, 20, 9, 16, 2);
        outer.CloseFigure();
        using var orange = new SolidBrush(Color.FromArgb(249, 115, 22));
        FillAndOutline(g, orange, outer);
        using var yellow = new SolidBrush(Color.FromArgb(253, 224, 71));
        g.FillEllipse(yellow, 12, 16, 8, 11);
    }

    private static void DrawAnimalHead(Graphics g, Color color)
    {
        using var head = new GraphicsPath();
        head.AddEllipse(4, 8, 24, 22);
        using var fill = new SolidBrush(color);
        FillAndOutline(g, fill, head);
        using var eye = new SolidBrush(Outline);
        g.FillEllipse(eye, 11, 16, 3, 4);
        g.FillEllipse(eye, 18, 16, 3, 4);
    }

    private static void DrawCat(Graphics g)
    {
        using var ears = new GraphicsPath();
        ears.AddPolygon(new PointF[] { new(6, 12), new(8, 2), new(14, 9) });
        ears.CloseFigure();
        ears.AddPolygon(new PointF[] { new(26, 12), new(24, 2), new(18, 9) });
        using var gray = new SolidBrush(Color.FromArgb(156, 163, 175));
        FillAndOutline(g, gray, ears);
        DrawAnimalHead(g, Color.FromArgb(156, 163, 175));
        using var pen = new Pen(Outline, 1.2f);
        g.DrawLine(pen, 2, 20, 9, 21);
        g.DrawLine(pen, 2, 24, 9, 23);
        g.DrawLine(pen, 30, 20, 23, 21);
        g.DrawLine(pen, 30, 24, 23, 23);
    }

    private static void DrawDog(Graphics g)
    {
        using var ears = new GraphicsPath();
        ears.AddEllipse(1, 8, 8, 14);
        ears.AddEllipse(23, 8, 8, 14);
        using var brown = new SolidBrush(Color.FromArgb(120, 83, 44));
        FillAndOutline(g, brown, ears);
        DrawAnimalHead(g, Color.FromArgb(180, 130, 80));
        using var nose = new SolidBrush(Outline);
        g.FillEllipse(nose, 14, 21, 4, 3);
    }

    private static void DrawBear(Graphics g)
    {
        using var ears = new GraphicsPath();
        ears.AddEllipse(4, 3, 8, 8);
        ears.AddEllipse(20, 3, 8, 8);
        using var brown = new SolidBrush(Color.FromArgb(146, 100, 57));
        FillAndOutline(g, brown, ears);
        DrawAnimalHead(g, Color.FromArgb(146, 100, 57));
        using var muzzle = new SolidBrush(Color.FromArgb(222, 190, 150));
        g.FillEllipse(muzzle, 12, 20, 8, 6);
        using var nose = new SolidBrush(Outline);
        g.FillEllipse(nose, 14.5f, 21, 3, 2.4f);
    }

    private static void DrawRabbit(Graphics g)
    {
        using var ears = new GraphicsPath();
        ears.AddEllipse(8, 0, 6, 14);
        ears.AddEllipse(18, 0, 6, 14);
        using var white = new SolidBrush(Color.White);
        FillAndOutline(g, white, ears);
        using var pink = new SolidBrush(Color.FromArgb(249, 168, 212));
        g.FillEllipse(pink, 10, 3, 2, 8);
        g.FillEllipse(pink, 20, 3, 2, 8);
        DrawAnimalHead(g, Color.White);
    }

    private static void DrawFrog(Graphics g)
    {
        using var bumps = new GraphicsPath();
        bumps.AddEllipse(5, 4, 9, 9);
        bumps.AddEllipse(18, 4, 9, 9);
        using var green = new SolidBrush(Color.FromArgb(74, 222, 128));
        FillAndOutline(g, green, bumps);
        using var head = new GraphicsPath();
        head.AddEllipse(3, 10, 26, 19);
        FillAndOutline(g, green, head);
        using var white = new SolidBrush(Color.White);
        g.FillEllipse(white, 7, 6, 5, 5);
        g.FillEllipse(white, 20, 6, 5, 5);
        using var eye = new SolidBrush(Outline);
        g.FillEllipse(eye, 9, 7.5f, 2.4f, 2.4f);
        g.FillEllipse(eye, 22, 7.5f, 2.4f, 2.4f);
        using var pen = new Pen(Outline, 1.4f);
        g.DrawArc(pen, 10, 16, 12, 8, 20, 140);
    }

    private static void DrawGhost(Graphics g)
    {
        using var path = new GraphicsPath();
        path.AddArc(6, 3, 20, 20, 180, 180);
        path.AddLine(26, 13, 26, 27);
        path.AddLine(26, 27, 22.7f, 24);
        path.AddLine(22.7f, 24, 19.3f, 28);
        path.AddLine(19.3f, 28, 16, 24);
        path.AddLine(16, 24, 12.7f, 28);
        path.AddLine(12.7f, 28, 9.3f, 24);
        path.AddLine(9.3f, 24, 6, 27);
        path.CloseFigure();
        using var white = new SolidBrush(Color.White);
        FillAndOutline(g, white, path);
        using var eye = new SolidBrush(Outline);
        g.FillEllipse(eye, 11, 11, 3.4f, 4.6f);
        g.FillEllipse(eye, 18, 11, 3.4f, 4.6f);
    }

    private static void DrawRobotCat(Graphics g)
    {
        using var head = new GraphicsPath();
        head.AddEllipse(3, 4, 26, 25);
        using var blue = new SolidBrush(Color.FromArgb(59, 130, 246));
        FillAndOutline(g, blue, head);
        using var face = new SolidBrush(Color.White);
        g.FillEllipse(face, 6, 12, 20, 15);
        using var eyeWhite = new SolidBrush(Color.White);
        g.FillEllipse(eyeWhite, 10, 6, 6, 8);
        g.FillEllipse(eyeWhite, 16, 6, 6, 8);
        using var outlinePen = OutlinePen();
        g.DrawEllipse(outlinePen, 10, 6, 6, 8);
        g.DrawEllipse(outlinePen, 16, 6, 6, 8);
        using var eye = new SolidBrush(Outline);
        g.FillEllipse(eye, 12.5f, 8.5f, 2, 3);
        g.FillEllipse(eye, 17.5f, 8.5f, 2, 3);
        using var nose = new SolidBrush(Color.FromArgb(220, 38, 38));
        g.FillEllipse(nose, 14.4f, 13, 3.2f, 3.2f);
        using var pen = new Pen(Outline, 1.2f);
        g.DrawLine(pen, 16, 16.5f, 16, 24);
        g.DrawLine(pen, 4, 17, 11, 18.5f);
        g.DrawLine(pen, 4, 22, 11, 21.5f);
        g.DrawLine(pen, 28, 17, 21, 18.5f);
        g.DrawLine(pen, 28, 22, 21, 21.5f);
    }

    private static void DrawRobot(Graphics g)
    {
        using var pen = OutlinePen();
        g.DrawLine(pen, 16, 6, 16, 2);
        using var antenna = new SolidBrush(Color.FromArgb(220, 38, 38));
        g.FillEllipse(antenna, 14.4f, 0, 3.2f, 3.2f);
        using var head = new GraphicsPath();
        head.AddPath(RoundedRect(5, 6, 22, 20, 4), false);
        using var gray = new SolidBrush(Color.FromArgb(203, 213, 225));
        FillAndOutline(g, gray, head);
        using var eye = new SolidBrush(Color.FromArgb(37, 99, 235));
        g.FillRectangle(eye, 9, 12, 5, 5);
        g.FillRectangle(eye, 18, 12, 5, 5);
        using var mouthPen = new Pen(Outline, 1.4f);
        g.DrawLine(mouthPen, 10, 21.5f, 22, 21.5f);
        g.DrawLine(mouthPen, 13, 19.5f, 13, 23.5f);
        g.DrawLine(mouthPen, 16, 19.5f, 16, 23.5f);
        g.DrawLine(mouthPen, 19, 19.5f, 19, 23.5f);
    }

    private static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        var path = new GraphicsPath();
        path.AddArc(x, y, r * 2, r * 2, 180, 90);
        path.AddArc(x + w - (r * 2), y, r * 2, r * 2, 270, 90);
        path.AddArc(x + w - (r * 2), y + h - (r * 2), r * 2, r * 2, 0, 90);
        path.AddArc(x, y + h - (r * 2), r * 2, r * 2, 90, 90);
        path.CloseFigure();
        return path;
    }
}
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 全綠（166 + 8 = 174；理論 2 案例）。

- [ ] **Step 5: Commit**

```text
feat: CursorGallery - 16 個程式繪製內建游標圖案

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

### Task 4: CursorImportService + FavoriteCursors（TDD）

**Files:**
- Modify: `Models/AppSettings.cs`（新增 `FavoriteCursors`）
- Create: `Services/CursorImportService.cs`
- Test: `tests/MousePilot.Tests/CursorImportServiceTests.cs`；`tests/MousePilot.Tests/SettingsServiceTests.cs`（加一測試）

**Interfaces:**
- Produces（Task 5/Phase 8 依賴，逐字）:
  - `AppSettings`：`public List<string> FavoriteCursors { get; set; } = new();`
  - `sealed record CursorImportResult(bool Success, string? StoredPath, string? Error, int? Width, int? Height)`
  - `class CursorImportService`（不 sealed）：建構子 `CursorImportService(string? storageDir = null)`（null → `%AppData%\MousePilot\Cursors`）；`static readonly string[] SupportedExtensions`；`public virtual CursorImportResult Import(string sourcePath)`；`public virtual bool Remove(string storedPath)`（僅允許刪 storage dir 內檔案）。

- [ ] **Step 1: 寫失敗測試**

`tests/MousePilot.Tests/CursorImportServiceTests.cs`：

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using MousePilot.Services;

namespace MousePilot.Tests;

public sealed class CursorImportServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "MousePilotCursorTests", Guid.NewGuid().ToString("N"));
    private readonly string _sourceDir;

    public CursorImportServiceTests()
    {
        _sourceDir = Path.Combine(_dir, "src");
        Directory.CreateDirectory(_sourceDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private CursorImportService Create() => new(Path.Combine(_dir, "store"));

    private string WritePng(string name = "test.png")
    {
        var path = Path.Combine(_sourceDir, name);
        using var bmp = new Bitmap(4, 4);
        bmp.SetPixel(1, 1, Color.Red);
        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    [Fact]
    public void 匯入PNG成功並複製到儲存目錄()
    {
        var service = Create();
        var result = service.Import(WritePng());

        Assert.True(result.Success);
        Assert.NotNull(result.StoredPath);
        Assert.True(File.Exists(result.StoredPath));
        Assert.StartsWith(Path.Combine(_dir, "store"), result.StoredPath);
        Assert.Equal(4, result.Width);
        Assert.Equal(4, result.Height);
    }

    [Fact]
    public void 同名檔案自動編號不覆蓋()
    {
        var service = Create();
        var first = service.Import(WritePng());
        var second = service.Import(WritePng());

        Assert.True(second.Success);
        Assert.NotEqual(first.StoredPath, second.StoredPath);
        Assert.True(File.Exists(first.StoredPath));
        Assert.True(File.Exists(second.StoredPath));
    }

    [Fact]
    public void 檔案不存在回傳錯誤()
    {
        var result = Create().Import(Path.Combine(_sourceDir, "nope.png"));
        Assert.False(result.Success);
        Assert.Contains("找不到", result.Error);
    }

    [Fact]
    public void 不支援的副檔名回傳錯誤()
    {
        var path = Path.Combine(_sourceDir, "a.txt");
        File.WriteAllText(path, "x");
        var result = Create().Import(path);
        Assert.False(result.Success);
        Assert.Contains("不支援", result.Error);
    }

    [Fact]
    public void 損毀圖片回傳錯誤且不留殘檔()
    {
        var path = Path.Combine(_sourceDir, "broken.png");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5 });
        var service = Create();

        var result = service.Import(path);

        Assert.False(result.Success);
        Assert.Contains("損毀", result.Error);
        Assert.Empty(Directory.Exists(Path.Combine(_dir, "store"))
            ? Directory.GetFiles(Path.Combine(_dir, "store"))
            : Array.Empty<string>());
    }

    [Fact]
    public void 匯入cur取得尺寸與成功()
    {
        using var bmp = new Bitmap(8, 8);
        bmp.SetPixel(0, 0, Color.Red);
        var curPath = Path.Combine(_sourceDir, "a.cur");
        File.WriteAllBytes(curPath, CurFileFormat.Write(bmp, 1, 1));

        var result = Create().Import(curPath);

        Assert.True(result.Success);
        Assert.Equal(8, result.Width);
    }

    [Fact]
    public void 損毀ani仍匯入成功但無尺寸()
    {
        var aniPath = Path.Combine(_sourceDir, "a.ani");
        File.WriteAllBytes(aniPath, new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0 });

        var result = Create().Import(aniPath);

        Assert.True(result.Success);  // 優雅降級：Windows 可能仍能載入，僅預覽不可用
        Assert.Null(result.Width);
    }

    [Fact]
    public void Remove只刪儲存目錄內的檔案()
    {
        var service = Create();
        var stored = service.Import(WritePng()).StoredPath!;
        var outside = WritePng("outside.png");

        Assert.True(service.Remove(stored));
        Assert.False(File.Exists(stored));
        Assert.False(service.Remove(outside));   // 目錄外 → 拒絕
        Assert.True(File.Exists(outside));
    }
}
```

`tests/MousePilot.Tests/SettingsServiceTests.cs` 新增：

```csharp
    [Fact]
    public void 收藏清單可保存與載回()
    {
        var service = new SettingsService(SettingsPath);
        service.Save(new AppSettings { FavoriteCursors = { "preset:Heart", "file:cat.png" } });

        var loaded = new SettingsService(SettingsPath).Load().Settings;

        Assert.Equal(new[] { "preset:Heart", "file:cat.png" }, loaded.FavoriteCursors);
        Assert.Contains("\"favoriteCursors\"", File.ReadAllText(SettingsPath));
    }
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 編譯失敗。

- [ ] **Step 3: 實作**

`Models/AppSettings.cs`：在 `CursorPreset` 屬性之後新增：

```csharp
    public List<string> FavoriteCursors { get; set; } = new();
```

`Services/CursorImportService.cs`：

```csharp
using System.Drawing;
using System.IO;

namespace MousePilot.Services;

public sealed record CursorImportResult(bool Success, string? StoredPath, string? Error, int? Width, int? Height);

/// <summary>
/// 匯入游標檔案：驗證副檔名、複製到儲存目錄（同名自動編號）、探測尺寸供顯示。
/// 錯誤一律回 Result（規格 §21 不 crash）；ANI 解析失敗優雅降級（仍匯入、僅無預覽尺寸）。
/// </summary>
public class CursorImportService
{
    public static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".cur", ".ani" };

    private readonly string _storageDir;

    public CursorImportService(string? storageDir = null)
    {
        _storageDir = storageDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MousePilot", "Cursors");
    }

    public virtual CursorImportResult Import(string sourcePath)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                return new CursorImportResult(false, null, "找不到檔案。", null, null);
            }

            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (!SupportedExtensions.Contains(ext))
            {
                return new CursorImportResult(false, null, $"不支援的檔案格式（{ext}）。", null, null);
            }

            Directory.CreateDirectory(_storageDir);
            var destPath = UniqueDestPath(Path.GetFileName(sourcePath));
            File.Copy(sourcePath, destPath);

            var (width, height, probeError) = Probe(destPath, ext);
            if (probeError is not null)
            {
                File.Delete(destPath); // 圖片損毀 → 不留殘檔
                return new CursorImportResult(false, null, probeError, null, null);
            }

            return new CursorImportResult(true, destPath, null, width, height);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CursorImportResult(false, null, $"匯入失敗：{ex.Message}", null, null);
        }
    }

    /// <summary>僅允許刪除儲存目錄內的檔案（防止誤刪任意路徑）。</summary>
    public virtual bool Remove(string storedPath)
    {
        try
        {
            var full = Path.GetFullPath(storedPath);
            if (!full.StartsWith(Path.GetFullPath(_storageDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            File.Delete(full);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string UniqueDestPath(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var candidate = Path.Combine(_storageDir, fileName);
        var n = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(_storageDir, $"{name} ({n++}){ext}");
        }

        return candidate;
    }

    private static (int? Width, int? Height, string? Error) Probe(string path, string ext)
    {
        switch (ext)
        {
            case ".cur":
            {
                var read = CurFileFormat.TryReadFirstImage(File.ReadAllBytes(path));
                if (read is null)
                {
                    return (null, null, "CUR 檔案損毀或格式不支援。");
                }

                read.Value.Image.Dispose();
                return (read.Value.Info.Width, read.Value.Info.Height, null);
            }

            case ".ani":
            {
                var read = CurFileFormat.TryReadAniFirstFrame(File.ReadAllBytes(path));
                if (read is null)
                {
                    return (null, null, null); // 優雅降級：僅無預覽，Windows 仍可能載入
                }

                read.Value.Image.Dispose();
                return (read.Value.Info.Width, read.Value.Info.Height, null);
            }

            default:
            {
                try
                {
                    using var bmp = new Bitmap(path);
                    return (bmp.Width, bmp.Height, null);
                }
                catch (ArgumentException)
                {
                    return (null, null, "圖片損毀或格式不支援。");
                }
            }
        }
    }
}
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 全綠（174 + 8 + 1 = 183）。

- [ ] **Step 5: Commit**

```text
feat: CursorImportService 與收藏資料層 - 匯入落地與探測

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

### Task 5: VM 與 XAML（匯入/移除/顯示，TDD）

**Files:**
- Modify: `ViewModels/MainViewModel.cs`
- Modify: `Views/MainWindow.xaml`（游標卡）
- Test: `tests/MousePilot.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `CursorImportService`（Task 4）。
- Produces（Phase 8/9 依賴）: `MainViewModel` 建構子第六、七參數 `CursorImportService? cursorImportService = null, Func<string?>? cursorFilePicker = null`（picker null → 生產路徑用 `Microsoft.Win32.OpenFileDialog`；兩者建構期皆無副作用，**既有測試建構點不需補參數**）；屬性 `string CursorFileText`（未選擇 → "未選擇"；已選 → "檔名（WxH）" 或 "檔名"）；命令 `ImportCursorCommand`（picker 回 null = 取消 → 無動作；失敗 → Notice）、`RemoveCursorCommand`（CanExecute = `Settings.CursorFile` 非空；刪檔 + 清空設定）。

- [ ] **Step 1: 寫失敗測試**

`tests/MousePilot.Tests/MainViewModelTests.cs` 新增：

```csharp
    private sealed class FakeCursorImportService : CursorImportService
    {
        public CursorImportResult NextResult = new(true, @"C:\store\cat.png", null, 32, 32);
        public List<string> Imported { get; } = new();
        public List<string> Removed { get; } = new();

        public FakeCursorImportService() : base(@"C:\store") { }

        public override CursorImportResult Import(string sourcePath)
        {
            Imported.Add(sourcePath);
            return NextResult;
        }

        public override bool Remove(string storedPath)
        {
            Removed.Add(storedPath);
            return true;
        }
    }

    private MainViewModel CreateVmWithCursor(FakeCursorImportService cursor, string? pickedFile)
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
            new NoOpStartupService(),
            new HotkeyHarness().Service,
            cursor,
            () => pickedFile);
    }

    [Fact]
    public void 匯入游標圖片成功更新設定與顯示()
    {
        var cursor = new FakeCursorImportService();
        var vm = CreateVmWithCursor(cursor, @"C:\pics\cat.png");

        vm.ImportCursorCommand.Execute(null);

        Assert.Equal(@"C:\pics\cat.png", cursor.Imported.Single());
        Assert.Equal(@"C:\store\cat.png", vm.Settings.CursorFile);
        Assert.Equal("cat.png（32x32）", vm.CursorFileText);
        Assert.True(vm.RemoveCursorCommand.CanExecute(null));
    }

    [Fact]
    public void 取消選檔不做任何事()
    {
        var cursor = new FakeCursorImportService();
        var vm = CreateVmWithCursor(cursor, pickedFile: null);

        vm.ImportCursorCommand.Execute(null);

        Assert.Empty(cursor.Imported);
        Assert.Equal("", vm.Settings.CursorFile);
    }

    [Fact]
    public void 匯入失敗顯示提示不更新設定()
    {
        var cursor = new FakeCursorImportService
        {
            NextResult = new CursorImportResult(false, null, "圖片損毀或格式不支援。", null, null),
        };
        var vm = CreateVmWithCursor(cursor, @"C:\pics\broken.png");

        vm.ImportCursorCommand.Execute(null);

        Assert.Equal("", vm.Settings.CursorFile);
        Assert.Contains("損毀", vm.Notice);
        Assert.False(vm.RemoveCursorCommand.CanExecute(null));
    }

    [Fact]
    public void 移除游標圖片刪檔並清空設定()
    {
        var cursor = new FakeCursorImportService();
        var vm = CreateVmWithCursor(cursor, @"C:\pics\cat.png");
        vm.ImportCursorCommand.Execute(null);

        vm.RemoveCursorCommand.Execute(null);

        Assert.Equal(@"C:\store\cat.png", cursor.Removed.Single());
        Assert.Equal("", vm.Settings.CursorFile);
        Assert.Equal("未選擇", vm.CursorFileText);
        Assert.False(vm.RemoveCursorCommand.CanExecute(null));
    }
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/MousePilot.Tests`
Expected: 編譯失敗（VM 無第六、七參數/屬性/命令）。

- [ ] **Step 3: 實作（`ViewModels/MainViewModel.cs` 修改）**

1. 欄位與建構子第六、七參數：

```csharp
    private readonly CursorImportService _cursorImportService;
    private readonly Func<string?> _cursorFilePicker;
```

```csharp
    public MainViewModel(
        SettingsService settingsService,
        Func<AppSettings, IdleDetectionService>? idleServiceFactory = null,
        Func<AppSettings, IdleDetectionService, MouseMovementService>? movementServiceFactory = null,
        StartupService? startupService = null,
        HotkeyService? hotkeyService = null,
        CursorImportService? cursorImportService = null,
        Func<string?>? cursorFilePicker = null)
```

建構子內（hotkey 註冊區塊之後）加：

```csharp
        _cursorImportService = cursorImportService ?? new CursorImportService();
        _cursorFilePicker = cursorFilePicker ?? PickCursorFile;
        RefreshCursorFileText();
```

2. 新增屬性（`[ObservableProperty] private string _cursorFileText = "未選擇";` 加入既有 ObservableProperty 區塊）與方法/命令（放在 `RestoreCursorHotkeyText` 之後）：

```csharp
    /// <summary>匯入游標圖片（規格 §7/§8；套用到 Windows 全域為 Phase 9）。</summary>
    [RelayCommand]
    private void ImportCursor()
    {
        if (_cursorFilePicker() is not { } path)
        {
            return; // 使用者取消選檔
        }

        var result = _cursorImportService.Import(path);
        if (!result.Success)
        {
            Notice = result.Error ?? "匯入失敗。";
            return;
        }

        Settings.CursorFile = result.StoredPath!;
        Settings.CursorPreset = "";
        _lastImportSize = (result.Width, result.Height);
        RefreshCursorFileText();
        RemoveCursorCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRemoveCursor))]
    private void RemoveCursor()
    {
        _cursorImportService.Remove(Settings.CursorFile);
        Settings.CursorFile = "";
        _lastImportSize = (null, null);
        RefreshCursorFileText();
        RemoveCursorCommand.NotifyCanExecuteChanged();
    }

    private bool CanRemoveCursor() => Settings.CursorFile.Length > 0;

    private (int? Width, int? Height) _lastImportSize;

    private void RefreshCursorFileText()
    {
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

    private static string? PickCursorFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "選擇游標圖片",
            Filter = "游標與圖片|*.png;*.jpg;*.jpeg;*.bmp;*.cur;*.ani|所有檔案|*.*",
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
```

3. `Views/MainWindow.xaml` 游標卡整塊替換為：

```xml
                <StackPanel>
                    <TextBlock Text="游標設定" Style="{StaticResource CardTitle}"/>
                    <TextBlock Foreground="#6B7280" Margin="0,0,0,8">
                        <Run Text="已匯入："/><Run Text="{Binding CursorFileText, Mode=OneWay}"/>
                    </TextBlock>
                    <StackPanel Orientation="Horizontal">
                        <Button Content="匯入圖片" Command="{Binding ImportCursorCommand}" Padding="12,6" Margin="0,0,8,0"/>
                        <Button Content="移除圖片" Command="{Binding RemoveCursorCommand}" Padding="12,6" Margin="0,0,8,0"/>
                        <Button Content="套用" IsEnabled="False" Padding="12,6" Margin="0,0,8,0"
                                ToolTip="Phase 9 提供"/>
                        <Button Content="恢復 Windows 游標" IsEnabled="False" Padding="12,6"
                                ToolTip="Phase 9 提供"/>
                    </StackPanel>
                    <TextBlock Text="預設圖案庫與預覽將於後續版本提供。" Foreground="#9CA3AF" Margin="0,8,0,0" FontSize="11"/>
                </StackPanel>
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `dotnet test tests/MousePilot.Tests`（183 + 4 = 187 全綠）、`dotnet build -c Release`（0 error 0 warning）。

- [ ] **Step 5: Commit**

```text
feat: 游標匯入 UI - 匯入/移除/檔名顯示

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

### Task 6: Phase 收尾

**Files:**
- Modify: `CHANGELOG.md`
- Modify: `docs/superpowers/plans/2026-08-20-mousepilot-master-plan.md`（Phase 7 → ✅ 完成、細部計畫文件欄填 `2026-08-21-phase7-cursor-import.md`）

- [ ] **Step 1: CHANGELOG [Unreleased]「### 新增」補上**

```markdown
- 游標匯入（Phase 7）：支援 PNG/JPG/JPEG/BMP/CUR/ANI 匯入至 %AppData%\MousePilot\Cursors\（透明裁切/等比縮放/JPG 去背/.cur 讀寫/ANI 首格預覽，損毀不 crash）；16 個程式繪製內建圖案（基本 8 + 可愛 8，含藍色機器貓風格）；收藏資料層。
```

- [ ] **Step 2: Master Plan 更新（僅 Phase 7 列與細部計畫文件欄）**

- [ ] **Step 3: 最終驗證**

```powershell
dotnet build -c Release
dotnet test tests/MousePilot.Tests
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

- [ ] **Step 4: Commit**

```text
docs: 更新 CHANGELOG 與進度總表 - Phase 7 完成

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

## Phase 7 完成定義

- [ ] build 0 error、測試全綠（預期 187）、publish 成功。
- [ ] 單元測試涵蓋：裁切/去背（Chebyshev 邊界）/等比置中、.cur 寫讀往返（像素+hotspot）、損毀/截斷降級、ANI 首格、16 圖案結構與可繪製性、匯入落地/編號/損毀清理/Remove 目錄防護、收藏 round-trip、VM 匯入/取消/失敗/移除。
- [ ] **使用者實機手動驗證（規格 §34 案例 13~17）：**
  1. 匯入 PNG（含透明）→ 顯示檔名與尺寸、檔案出現在 `%AppData%\MousePilot\Cursors\`（案例 13）。
  2. 匯入 JPG、BMP 各一 → 成功（案例 14/15）。
  3. 匯入 .cur → 成功且尺寸正確（案例 16）。
  4. 匯入 .ani → 成功（案例 17；預覽尺寸可能空白屬正常）。
  5. 匯入損毀檔（改副檔名的文字檔）→ Notice 提示、不 crash、儲存目錄無殘檔。
  6. 「移除圖片」→ 檔案刪除、顯示回「未選擇」。
