using Microsoft.Win32;
using MousePilot.Services;

namespace MousePilot.Tests;

public sealed class StartupServiceTests : IDisposable
{
    private readonly string _testRoot = @"Software\MousePilotTests\" + Guid.NewGuid().ToString("N");

    private string RunKeyPath => _testRoot + @"\Run";

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
        => new(exePath, RunKeyPath);

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
        var service = new StartupService("", RunKeyPath);
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
}
