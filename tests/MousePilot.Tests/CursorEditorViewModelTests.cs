using System.Drawing;
using System.IO;
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
