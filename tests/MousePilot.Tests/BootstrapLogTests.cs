using System.IO;
using MousePilot.Services;

namespace MousePilot.Tests;

/// <summary>
/// BootstrapLog：LogService 建立前的極早期開機紀錄（規格：啟動失敗一定要留得下完整線索）。
/// 不依賴 LogService——驗證「正式 log 尚未建立前也能寫入」。
/// </summary>
public sealed class BootstrapLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "MousePilotBootTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string PrimaryPath => Path.Combine(_dir, "Logs", "mousepilot-bootstrap.log");
    private string FallbackPath => Path.Combine(_dir, "fallback", "mousepilot-bootstrap.log");

    /// <summary>把路徑的父層做成「檔案」，令 CreateDirectory 必然失敗。</summary>
    private string BlockedPath(string name)
    {
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, name);
        File.WriteAllText(blocker, "");
        return Path.Combine(blocker, "mousepilot-bootstrap.log");
    }

    [Fact]
    public void 寫入主要路徑並自動建目錄()
    {
        var written = BootstrapLog.Write("App ctor", PrimaryPath, FallbackPath);

        Assert.Equal(PrimaryPath, written);
        var text = File.ReadAllText(PrimaryPath);
        Assert.Contains("App ctor", text);
        Assert.False(File.Exists(FallbackPath)); // 主要路徑成功就不碰備援
    }

    [Fact]
    public void 主要路徑不可用時退回備援()
    {
        var written = BootstrapLog.Write("OnStartup 進入", BlockedPath("blocked-primary"), FallbackPath);

        Assert.Equal(FallbackPath, written);
        Assert.Contains("OnStartup 進入", File.ReadAllText(FallbackPath));
    }

    [Fact]
    public void 兩路徑都失敗時靜默不拋()
    {
        string? written = null;
        var ex = Record.Exception(() =>
            written = BootstrapLog.Write("x", BlockedPath("blocked-primary"), BlockedPath("blocked-fallback")));

        Assert.Null(ex);      // 紀錄失敗不得造成第二次例外（規格）
        Assert.Null(written); // 兩路徑都沒寫成
    }

    [Fact]
    public void 超過大小上限時重寫而非續寫()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PrimaryPath)!);
        File.WriteAllText(PrimaryPath, new string('x', (int)BootstrapLog.MaxBytes + 1));

        BootstrapLog.Write("重啟", PrimaryPath, FallbackPath);

        var text = File.ReadAllText(PrimaryPath);
        Assert.Contains("重啟", text);
        Assert.DoesNotContain("xxx", text); // crash loop 不得讓 bootstrap log 無限成長
    }
}
