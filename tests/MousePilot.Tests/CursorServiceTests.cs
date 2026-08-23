using System.IO;
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
