using Microsoft.Win32;
using MousePilot.Services;

namespace MousePilot.Tests;

[Collection("RealRunKeyCanary")]
public sealed class StartupServiceTests : IDisposable
{
    private readonly string _testRoot = @"Software\MousePilotTests\" + Guid.NewGuid().ToString("N");

    private string RunKeyPath => _testRoot + @"\Run";

    private string ApprovedKeyPath => _testRoot + @"\StartupApproved";

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
        => new(exePath, RunKeyPath, "MousePilot", ApprovedKeyPath);

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
        var service = new StartupService("", RunKeyPath, "MousePilot", ApprovedKeyPath);
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

    [Fact]
    public void 工作管理員停用旗標時IsEnabled為false()
    {
        var service = Create();
        service.Enable();
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(ApprovedKeyPath))
        {
            key.SetValue("MousePilot", new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        }

        Assert.False(service.IsEnabled());
    }

    [Fact]
    public void Enable清除停用旗標()
    {
        var service = Create();
        service.Enable();
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(ApprovedKeyPath))
        {
            key.SetValue("MousePilot", new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        }

        Assert.True(service.Enable());

        Assert.True(service.IsEnabled());
        using var approved = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ApprovedKeyPath);
        Assert.Null(approved?.GetValue("MousePilot"));
    }

    [Fact]
    public void Disable清除停用旗標()
    {
        var service = Create();
        service.Enable();
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(ApprovedKeyPath))
        {
            key.SetValue("MousePilot", new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        }

        Assert.True(service.Disable());

        using var approved = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ApprovedKeyPath);
        Assert.Null(approved?.GetValue("MousePilot"));
    }
}
