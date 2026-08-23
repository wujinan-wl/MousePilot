using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
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
    public void 損毀cur回傳錯誤且不留殘檔()
    {
        var path = Path.Combine(_sourceDir, "bad.cur");
        File.WriteAllBytes(path, new byte[] { 0, 0, 9, 9, 1, 0 });

        var result = Create().Import(path);

        Assert.False(result.Success);
        Assert.Contains("CUR", result.Error);
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "store")));
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

    [Fact]
    public void ListStored列出儲存目錄內支援的檔案()
    {
        var service = Create();
        Assert.Empty(service.ListStored()); // 目錄尚未建立

        var stored = service.Import(WritePng()).StoredPath!;
        File.WriteAllText(Path.Combine(_dir, "store", "note.txt"), "x"); // 不支援副檔名應排除

        Assert.Equal(new[] { stored }, service.ListStored());
    }
}
