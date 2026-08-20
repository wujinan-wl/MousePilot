using System;
using System.IO;
using Xunit;
using MousePilot.Models;
using MousePilot.Services;
using MousePilot.ViewModels;

namespace MousePilot.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "MousePilotTests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void 初始狀態為已暫停()
    {
        var vm = new MainViewModel(new SettingsService(SettingsPath));
        Assert.Equal(MonitorStatus.Paused, vm.Status);
        Assert.Equal("已暫停", vm.StatusText);
    }

    [Fact]
    public void 設定損毀時顯示非侵入式提示()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ broken");

        var vm = new MainViewModel(new SettingsService(SettingsPath));

        Assert.Contains("預設值", vm.Notice);
        Assert.Equal(120, vm.Settings.IdleStartSeconds); // 已回到預設，程式未 crash
    }

    [Fact]
    public void SaveSettings會保存並夾制範圍()
    {
        var vm = new MainViewModel(new SettingsService(SettingsPath));
        vm.Settings.IdleStartSeconds = 999999;

        vm.SaveSettings();

        var reloaded = new SettingsService(SettingsPath).Load().Settings;
        Assert.Equal(86400, reloaded.IdleStartSeconds);
    }

    [Fact]
    public void Phase1階段啟動與暫停命令停用()
    {
        var vm = new MainViewModel(new SettingsService(SettingsPath));
        Assert.False(vm.StartCommand.CanExecute(null));
        Assert.False(vm.PauseCommand.CanExecute(null));
    }

    [Fact]
    public void SaveSettings路徑不可寫時不擲例外並顯示提示()
    {
        // 把「應為目錄」的位置先建成一般檔案，使 CreateDirectory/寫入必定失敗
        Directory.CreateDirectory(_dir);
        var blockedDir = Path.Combine(_dir, "blocked");
        File.WriteAllText(blockedDir, "occupy");
        var vm = new MainViewModel(new SettingsService(Path.Combine(blockedDir, "settings.json")));

        var ex = Record.Exception(() => vm.SaveSettings());

        Assert.Null(ex);
        Assert.Contains("保存失敗", vm.Notice);
    }

    [Theory]
    [InlineData(MonitorStatus.Paused, "已暫停")]
    [InlineData(MonitorStatus.Monitoring, "監控中")]
    [InlineData(MonitorStatus.UserActive, "使用者活動中")]
    [InlineData(MonitorStatus.WaitingToStart, "等待啟動")]
    [InlineData(MonitorStatus.AutoMoving, "自動移動中")]
    public void StatusText由Status派生(MonitorStatus status, string expected)
    {
        var vm = new MainViewModel(new SettingsService(SettingsPath));
        vm.Status = status;
        Assert.Equal(expected, vm.StatusText);
    }
}
