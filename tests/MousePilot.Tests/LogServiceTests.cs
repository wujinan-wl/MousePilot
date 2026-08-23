using System.IO;
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
