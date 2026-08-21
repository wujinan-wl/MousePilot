using System;
using System.IO;
using Xunit;
using MousePilot.Models;
using MousePilot.Services;
using MousePilot.ViewModels;

namespace MousePilot.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private sealed class TestClock
    {
        public uint Now;
        public uint LastInput;
    }

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "MousePilotTests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private MainViewModel CreateVm(bool autoStart, TestClock clock)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath,
            $"{{\"autoStartMonitoring\": {(autoStart ? "true" : "false")}, \"idleStartSeconds\": 5}}");
        return new MainViewModel(new SettingsService(SettingsPath),
            s => new IdleDetectionService(s, () => clock.Now, () => clock.LastInput, () => (7, 8)));
    }

    [Fact]
    public void 關閉自動開始時初始為已暫停()
    {
        var vm = CreateVm(autoStart: false, new TestClock());
        Assert.Equal(MonitorStatus.Paused, vm.Status);
        Assert.Equal("已暫停", vm.StatusText);
        Assert.True(vm.StartCommand.CanExecute(null));
        Assert.False(vm.PauseCommand.CanExecute(null));
    }

    [Fact]
    public void 開啟自動開始時建構後即在監控()
    {
        var clock = new TestClock { Now = 10_000, LastInput = 10_000 };
        var vm = CreateVm(autoStart: true, clock);
        Assert.Equal(MonitorStatus.UserActive, vm.Status);
        Assert.False(vm.StartCommand.CanExecute(null));
        Assert.True(vm.PauseCommand.CanExecute(null));
    }

    [Fact]
    public void 達門檻時觸發佔位事件並更新顯示()
    {
        var clock = new TestClock();
        var vm = CreateVm(autoStart: true, clock); // idleStart=5 秒
        clock.Now = 5_000;
        vm.IdleService.PollNow();

        Assert.Equal(1, vm.TriggerCount);
        Assert.Equal(MonitorStatus.WaitingToStart, vm.Status);
        Assert.Equal("等待啟動", vm.StatusText);
        Assert.Equal("X=7, Y=8", vm.MousePosition);
        Assert.Equal("30 秒", vm.NextMoveText);
        Assert.Equal("—", vm.FirstTriggerText);
    }

    [Fact]
    public void 監控中顯示第一次觸發倒數()
    {
        var clock = new TestClock();
        var vm = CreateVm(autoStart: true, clock);
        clock.Now = 3_000;
        vm.IdleService.PollNow();

        Assert.Equal(MonitorStatus.Monitoring, vm.Status);
        Assert.Equal(3.0, vm.IdleSeconds);
        Assert.Equal("2 秒", vm.FirstTriggerText);
        Assert.Equal("—", vm.NextMoveText);
    }

    [Fact]
    public void 游標讀取失敗時顯示破折號()
    {
        var clock = new TestClock();
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{\"autoStartMonitoring\": true, \"idleStartSeconds\": 5}");
        var vm = new MainViewModel(new SettingsService(SettingsPath),
            s => new IdleDetectionService(s, () => clock.Now, () => clock.LastInput, () => null));
        vm.IdleService.PollNow();
        Assert.Equal("—", vm.MousePosition);
    }

    [Fact]
    public void 暫停與啟動命令切換狀態()
    {
        var vm = CreateVm(autoStart: true, new TestClock());
        vm.PauseCommand.Execute(null);
        Assert.Equal(MonitorStatus.Paused, vm.Status);
        Assert.True(vm.StartCommand.CanExecute(null));

        vm.StartCommand.Execute(null);
        Assert.NotEqual(MonitorStatus.Paused, vm.Status);
        Assert.True(vm.PauseCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(MonitorStatus.Paused, "已暫停")]
    [InlineData(MonitorStatus.Monitoring, "監控中")]
    [InlineData(MonitorStatus.UserActive, "使用者活動中")]
    [InlineData(MonitorStatus.WaitingToStart, "等待啟動")]
    [InlineData(MonitorStatus.AutoMoving, "自動移動中")]
    public void StatusText由Status派生(MonitorStatus status, string expected)
    {
        var vm = CreateVm(autoStart: false, new TestClock());
        vm.Status = status;
        Assert.Equal(expected, vm.StatusText);
    }

    [Fact]
    public void 設定損毀時顯示非侵入式提示()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ broken");

        var vm = new MainViewModel(new SettingsService(SettingsPath),
            s => new IdleDetectionService(s, () => 0u, () => 0u, () => (0, 0)));

        Assert.Contains("預設值", vm.Notice);
        Assert.Equal(120, vm.Settings.IdleStartSeconds);
    }

    [Fact]
    public void SaveSettings會保存並夾制範圍()
    {
        var vm = CreateVm(autoStart: false, new TestClock());
        vm.Settings.IdleStartSeconds = 999999;

        vm.SaveSettings();

        var reloaded = new SettingsService(SettingsPath).Load().Settings;
        Assert.Equal(86400, reloaded.IdleStartSeconds);
    }

    [Fact]
    public void SaveSettings路徑不可寫時不擲例外並顯示提示()
    {
        Directory.CreateDirectory(_dir);
        var blockedDir = Path.Combine(_dir, "blocked");
        File.WriteAllText(blockedDir, "occupy");
        var vm = new MainViewModel(new SettingsService(Path.Combine(blockedDir, "settings.json")),
            s => new IdleDetectionService(s, () => 0u, () => 0u, () => (0, 0)));

        var ex = Record.Exception(() => vm.SaveSettings());

        Assert.Null(ex);
        Assert.Contains("保存失敗", vm.Notice);
    }
}
