using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using MousePilot.Models;
using MousePilot.Services;
using MousePilot.ViewModels;

namespace MousePilot.Tests;

[Collection("RealRunKeyCanary")]
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

    private sealed class MoveRecorder
    {
        public List<(int X, int Y)> Sent { get; } = new();
        public bool ThrowOnMove;
    }

    private sealed class HotkeyHarness
    {
        public List<(int Id, uint Mod, uint Vk)> Registered { get; } = new();
        public List<int> Unregistered { get; } = new();
        public HashSet<(uint Mod, uint Vk)> Occupied { get; } = new();
        public HotkeyService Service { get; }

        public HotkeyHarness()
        {
            Service = new HotkeyService(
                (id, mod, vk) =>
                {
                    if (Occupied.Contains((mod, vk)))
                    {
                        return false;
                    }

                    Registered.Add((id, mod, vk));
                    return true;
                },
                id => { Unregistered.Add(id); return true; });
        }
    }

    private MainViewModel CreateVm(bool autoStart, TestClock clock, MoveRecorder? recorder = null)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath,
            $"{{\"autoStartMonitoring\": {(autoStart ? "true" : "false")}, \"idleStartSeconds\": 5, \"returnToOriginalPosition\": false}}");
        return new MainViewModel(new SettingsService(SettingsPath),
            s => new IdleDetectionService(s, () => clock.Now, () => clock.LastInput, () => (7, 8)),
            (s, idle) => new MouseMovementService(
                s, idle,
                cursorProvider: () => (500, 300),
                boundsProvider: () => new ScreenBounds(0, 0, 1920, 1080),
                sendMove: (x, y) =>
                {
                    if (recorder?.ThrowOnMove == true) { throw new InvalidOperationException("boom"); }
                    recorder?.Sent.Add((x, y));
                    return true;
                },
                correctPosition: (_, _) => true,
                lastInputProvider: () => 0u,
                delay: (_, _) => Task.CompletedTask,
                randomIndexProvider: () => 3),
            new NoOpStartupService(),
            new HotkeyHarness().Service);
    }

    [Fact]
    public async Task 立即執行一次會送出移動()
    {
        var recorder = new MoveRecorder();
        var vm = CreateVm(autoStart: false, new TestClock(), recorder);
        await vm.MoveOnceCommand.ExecuteAsync(null);
        Assert.Single(recorder.Sent);
        Assert.Equal((503, 300), recorder.Sent[0]); // Random 模式 randomIndex=3 → 右 +3px
    }

    private MainViewModel CreateVmWithReturn(TestClock clock, MoveRecorder recorder)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath,
            "{\"autoStartMonitoring\": true, \"idleStartSeconds\": 5, \"returnToOriginalPosition\": true}");
        MainViewModel? vmRef = null;
        var cursor = (X: 500, Y: 300);
        var vm = new MainViewModel(new SettingsService(SettingsPath),
            s => new IdleDetectionService(s, () => clock.Now, () => clock.LastInput, () => cursor),
            (s, idle) => new MouseMovementService(
                s, idle,
                cursorProvider: () => cursor,
                boundsProvider: () => new ScreenBounds(0, 0, 1920, 1080),
                sendMove: (x, y) => { recorder.Sent.Add((x, y)); cursor = (x, y); return true; },
                correctPosition: (x, y) => { cursor = (x, y); return true; },
                lastInputProvider: () => 0u,
                delay: (_, ct) =>
                {
                    vmRef!.IdleService.PollNow();      // 模擬 300ms 等待期間的輪詢（此時狀態為 UserActive）
                    ct.ThrowIfCancellationRequested(); // 修正前會在此擲出（被誤取消）
                    return Task.CompletedTask;
                },
                randomIndexProvider: () => 3),
            new NoOpStartupService(),
            new HotkeyHarness().Service);
        vmRef = vm;
        return vm;
    }

    [Fact]
    public async Task 監控中手動執行不因輪詢誤取消返回()
    {
        var recorder = new MoveRecorder();
        var clock = new TestClock { Now = 1_000, LastInput = 1_000 }; // 剛有輸入 → UserActive
        var vm = CreateVmWithReturn(clock, recorder);
        recorder.Sent.Clear();                                        // 排除建構期間可能的干擾（應為空）

        await vm.MoveOnceCommand.ExecuteAsync(null);

        Assert.Equal(2, recorder.Sent.Count);                         // 去程 + 返回都完成
        Assert.Equal((503, 300), recorder.Sent[0]);
        Assert.Equal((500, 300), recorder.Sent[1]);
    }

    [Fact]
    public void 自動觸發時執行移動並累計次數()
    {
        var recorder = new MoveRecorder();
        var clock = new TestClock();
        var vm = CreateVm(autoStart: true, clock, recorder);
        clock.Now = 5_000;
        vm.IdleService.PollNow();          // 觸發 → MoveRequested → 移動（delay 為同步完成）
        Assert.Equal(1, vm.TriggerCount);
        Assert.Single(recorder.Sent);
    }

    [Fact]
    public async Task 移動例外不外拋僅顯示提示()
    {
        var recorder = new MoveRecorder { ThrowOnMove = true };
        var vm = CreateVm(autoStart: false, new TestClock(), recorder);
        var ex = await Record.ExceptionAsync(() => vm.MoveOnceCommand.ExecuteAsync(null));
        Assert.Null(ex);
        Assert.Contains("移動失敗", vm.Notice);
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
            s => new IdleDetectionService(s, () => clock.Now, () => clock.LastInput, () => null),
            null,
            new NoOpStartupService(),
            new HotkeyHarness().Service);
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
            s => new IdleDetectionService(s, () => 0u, () => 0u, () => (0, 0)),
            null,
            new NoOpStartupService(),
            new HotkeyHarness().Service);

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
            s => new IdleDetectionService(s, () => 0u, () => 0u, () => (0, 0)),
            null,
            new NoOpStartupService(),
            new HotkeyHarness().Service);

        var ex = Record.Exception(() => vm.SaveSettings());

        Assert.Null(ex);
        Assert.Contains("保存失敗", vm.Notice);
    }

    private sealed class FailingStartupService : StartupService
    {
        public FailingStartupService() : base(@"C:\Apps\MousePilot.exe", @"Software\MousePilotTests\unused\Run") { }

        public override bool Enable() => false;

        public override bool Disable() => false;

        public override bool? IsEnabled() => null;
    }

    private sealed class DisableFailsStartupService : StartupService
    {
        public DisableFailsStartupService() : base(@"C:\Apps\MousePilot.exe", @"Software\MousePilotTests\unused\Run") { }

        public override bool Enable() => true;

        public override bool Disable() => false;

        public override bool? IsEnabled() => true; // 已註冊 → ctor 同步回填 true
    }

    [Fact]
    public void 取消勾選失敗時顯示提示且維持勾選()
    {
        var vm = CreateVmWithStartup(new DisableFailsStartupService());
        Assert.True(vm.Settings.RunAtStartup); // ctor 同步回填

        vm.RunAtStartup = false;

        Assert.True(vm.Settings.RunAtStartup);
        Assert.True(vm.RunAtStartup);
        Assert.Contains("開機自動啟動", vm.Notice);
    }

    private sealed class NoOpStartupService : StartupService
    {
        public NoOpStartupService() : base(@"C:\Apps\MousePilot.exe", @"Software\MousePilotTests\noop\Run") { }

        public override bool Enable() => true;

        public override bool Disable() => true;

        public override bool? IsEnabled() => false; // 未註冊、不碰任何 Registry
    }

    private string _startupTestRoot = "";

    private StartupService CreateStartupService(string exePath = @"C:\Apps\MousePilot.exe")
    {
        if (_startupTestRoot.Length == 0)
        {
            _startupTestRoot = @"Software\MousePilotTests\" + Guid.NewGuid().ToString("N");
        }

        return new StartupService(exePath, _startupTestRoot + @"\Run");
    }

    private void CleanupStartupKey()
    {
        if (_startupTestRoot.Length > 0)
        {
            try
            {
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(_startupTestRoot, throwOnMissingSubKey: false);
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public void 勾選開機自啟寫入Registry並更新設定()
    {
        try
        {
            var startup = CreateStartupService();
            var vm = CreateVmWithStartup(startup);

            vm.RunAtStartup = true;

            Assert.True(vm.Settings.RunAtStartup);
            Assert.True(startup.IsEnabled());

            vm.RunAtStartup = false;

            Assert.False(vm.Settings.RunAtStartup);
            Assert.False(startup.IsEnabled());
        }
        finally
        {
            CleanupStartupKey();
        }
    }

    [Fact]
    public void Registry失敗時顯示提示且值不變()
    {
        var vm = CreateVmWithStartup(new FailingStartupService());

        vm.RunAtStartup = true;

        Assert.False(vm.Settings.RunAtStartup);   // 失敗 → 不更新
        Assert.False(vm.RunAtStartup);            // checkbox 讀回仍為 false
        Assert.Contains("開機自動啟動", vm.Notice);
    }

    [Fact]
    public void 啟動時同步Registry實際狀態並修復路徑()
    {
        try
        {
            CreateStartupService(@"C:\Old\MousePilot.exe").Enable(); // 外部已註冊、路徑過期
            var current = CreateStartupService(@"D:\New\MousePilot.exe");

            var vm = CreateVmWithStartup(current);                    // settings 檔內 runAtStartup 未設（false）

            Assert.True(vm.Settings.RunAtStartup);                    // 回填實際狀態
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(_startupTestRoot + @"\Run");
            Assert.Equal("\"D:\\New\\MousePilot.exe\"", key!.GetValue("MousePilot")); // 路徑已修復
        }
        finally
        {
            CleanupStartupKey();
        }
    }

    private MainViewModel CreateVmWithStartup(
        StartupService startup,
        HotkeyService? hotkey = null,
        CursorService? cursorService = null,
        Func<byte[], string?>? confirmedCurWriter = null,
        LogService? logService = null)
    {
        Directory.CreateDirectory(_dir);
        if (!File.Exists(SettingsPath)) // 呼叫端可能已預先寫入（含損毀內容）供測試場景使用，不覆蓋
        {
            File.WriteAllText(SettingsPath, "{\"autoStartMonitoring\": false, \"idleStartSeconds\": 5}");
        }

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
            startup,
            hotkey ?? new HotkeyHarness().Service,
            cursorService: cursorService ?? new FakeCursorService(), // 預設 fake：任何經 helper 的測試絕不觸真實 Win32 游標 API
            confirmedCurWriter: confirmedCurWriter ?? (_ => Path.Combine(_dir, "confirmed-cursor.cur")),
            logService: logService);
    }

    private MainViewModel CreateVmWithFailingMove(LogService? log = null)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath,
            "{\"autoStartMonitoring\": false, \"idleStartSeconds\": 5, \"returnToOriginalPosition\": true}");
        var clock = new TestClock();
        var cursor = (X: 500, Y: 300);
        return new MainViewModel(new SettingsService(SettingsPath),
            s => new IdleDetectionService(s, () => clock.Now, () => clock.LastInput, () => cursor),
            (s, idle) => new MouseMovementService(
                s, idle,
                cursorProvider: () => cursor,
                boundsProvider: () => new ScreenBounds(0, 0, 1920, 1080),
                sendMove: (_, _) => false,
                correctPosition: (_, _) => true,
                lastInputProvider: () => 0u,
                delay: (_, _) => Task.CompletedTask,
                randomIndexProvider: () => 3),
            new NoOpStartupService(),
            new HotkeyHarness().Service,
            cursorService: new FakeCursorService(),
            confirmedCurWriter: _ => Path.Combine(_dir, "confirmed-cursor.cur"),
            logService: log);
    }

    [Fact]
    public void 連續移動失敗三次進入錯誤狀態()
    {
        var vm = CreateVmWithFailingMove();
        vm.StartCommand.Execute(null);

        vm.MoveOnceCommand.Execute(null); // 1
        vm.MoveOnceCommand.Execute(null); // 2
        Assert.NotEqual(MonitorStatus.Error, vm.Status);
        vm.MoveOnceCommand.Execute(null); // 3

        Assert.Equal(MonitorStatus.Error, vm.Status);
        Assert.Contains("錯誤", vm.StatusText);
    }

    [Fact]
    public void 錯誤狀態由重新啟動解除()
    {
        var vm = CreateVmWithFailingMove();
        vm.StartCommand.Execute(null);
        for (var i = 0; i < 3; i++) { vm.MoveOnceCommand.Execute(null); }
        Assert.Equal(MonitorStatus.Error, vm.Status);

        vm.PauseCommand.Execute(null);
        vm.StartCommand.Execute(null); // 重新啟動 → latch 解除

        Assert.NotEqual(MonitorStatus.Error, vm.Status);
    }

    [Fact]
    public void 錯誤狀態暫停後顯示已暫停且可重啟()
    {
        var vm = CreateVmWithFailingMove();
        vm.StartCommand.Execute(null);
        for (var i = 0; i < 3; i++) { vm.MoveOnceCommand.Execute(null); }
        Assert.Equal(MonitorStatus.Error, vm.Status);

        vm.PauseCommand.Execute(null);

        Assert.Equal(MonitorStatus.Paused, vm.Status);
        Assert.Equal("已暫停", vm.StatusText);
        Assert.True(vm.StartCommand.CanExecute(null)); // UI 可重啟（review 修正）
    }

    [Fact]
    public void 移動失敗寫入log()
    {
        var dir = Path.Combine(_dir, "logs");
        var log = new LogService(dir, clock: () => DateTime.Now);
        var vm = CreateVmWithFailingMove(log);
        vm.StartCommand.Execute(null);

        vm.MoveOnceCommand.Execute(null);

        Assert.Contains("[ERROR]", File.ReadAllText(Path.Combine(dir, "mousepilot.log")));
    }

    [Fact]
    public void 設定損毀寫入log()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ 不是 JSON");
        var dir = Path.Combine(_dir, "logs");
        var log = new LogService(dir, clock: () => DateTime.Now);

        _ = CreateVmWithStartup(new NoOpStartupService(), logService: log);

        Assert.Contains("設定", File.ReadAllText(Path.Combine(dir, "mousepilot.log")));
    }

    [Fact]
    public void 編輯游標確認後刷新顯示()
    {
        var vm = CreateVmWithStartup(new NoOpStartupService());
        vm.AttachCursorEditorLauncher(editorVm =>
        {
            editorVm.SelectedSource = editorVm.Sources.First(s => s.Source.Id == "preset:Heart");
            editorVm.ConfirmCommand.Execute(null);
            return true;
        });

        vm.EditCursorCommand.Execute(null);

        Assert.Equal("Heart", vm.Settings.CursorPreset);
        Assert.Contains("愛心", vm.CursorFileText);
        Assert.Contains("32", vm.CursorFileText);
    }

    [Fact]
    public void 編輯游標取消不改設定()
    {
        var vm = CreateVmWithStartup(new NoOpStartupService());
        vm.AttachCursorEditorLauncher(_ => false);

        vm.EditCursorCommand.Execute(null);

        Assert.Equal("", vm.Settings.CursorPreset);
        Assert.Equal("未選擇", vm.CursorFileText);
    }

    [Fact]
    public void 未接上編輯器時命令不當機()
    {
        var vm = CreateVmWithStartup(new NoOpStartupService());
        var ex = Record.Exception(() => vm.EditCursorCommand.Execute(null));
        Assert.Null(ex);
    }

    [Fact]
    public void 啟動時依設定註冊兩組快捷鍵()
    {
        var hotkeys = new HotkeyHarness();
        var vm = CreateVmWithStartup(new NoOpStartupService(), hotkeys.Service);
        Assert.Contains((MainViewModel.ToggleHotkeyId, HotkeyParser.ModControl | HotkeyParser.ModAlt, 0x78u), hotkeys.Registered);
        Assert.Contains((MainViewModel.RestoreCursorHotkeyId, HotkeyParser.ModControl | HotkeyParser.ModAlt, 0x79u), hotkeys.Registered);
        Assert.Equal("", vm.Notice);
    }

    [Fact]
    public void 啟動時快捷鍵被占用顯示提示但不當機()
    {
        var hotkeys = new HotkeyHarness();
        hotkeys.Occupied.Add((HotkeyParser.ModControl | HotkeyParser.ModAlt, 0x78u));
        var vm = CreateVmWithStartup(new NoOpStartupService(), hotkeys.Service);
        Assert.Contains("占用", vm.Notice);
    }

    [Fact]
    public void 修改快捷鍵成功後更新設定並重新註冊()
    {
        var hotkeys = new HotkeyHarness();
        var vm = CreateVmWithStartup(new NoOpStartupService(), hotkeys.Service);

        vm.ToggleHotkeyText = "Ctrl+Shift+M";

        Assert.Equal("Ctrl+Shift+M", vm.Settings.ToggleHotkey);
        Assert.Contains((MainViewModel.ToggleHotkeyId, HotkeyParser.ModControl | HotkeyParser.ModShift, 0x4Du), hotkeys.Registered);
    }

    [Fact]
    public void 無效快捷鍵顯示提示且還原()
    {
        var vm = CreateVmWithStartup(new NoOpStartupService(), new HotkeyHarness().Service);

        vm.ToggleHotkeyText = "F9";

        Assert.Equal("Ctrl+Alt+F9", vm.Settings.ToggleHotkey);
        Assert.Equal("Ctrl+Alt+F9", vm.ToggleHotkeyText);
        Assert.Contains("修飾鍵", vm.Notice);
    }

    [Fact]
    public void 與另一組重複顯示提示且還原()
    {
        var vm = CreateVmWithStartup(new NoOpStartupService(), new HotkeyHarness().Service);

        vm.ToggleHotkeyText = "Ctrl+Alt+F10"; // 與恢復游標重複

        Assert.Equal("Ctrl+Alt+F9", vm.Settings.ToggleHotkey);
        Assert.Contains("重複", vm.Notice);
    }

    [Fact]
    public void 被占用時顯示提示且舊組合重新註冊()
    {
        var hotkeys = new HotkeyHarness();
        var vm = CreateVmWithStartup(new NoOpStartupService(), hotkeys.Service);
        hotkeys.Occupied.Add((HotkeyParser.ModControl | HotkeyParser.ModShift, 0x4Du));

        vm.ToggleHotkeyText = "Ctrl+Shift+M";

        Assert.Equal("Ctrl+Alt+F9", vm.Settings.ToggleHotkey);
        Assert.Contains("占用", vm.Notice);
        // 舊組合在失敗後重新註冊（最後一筆應為 Ctrl+Alt+F9）
        Assert.Equal((MainViewModel.ToggleHotkeyId, HotkeyParser.ModControl | HotkeyParser.ModAlt, 0x78u), hotkeys.Registered[^1]);
    }

    [Fact]
    public void F9快捷鍵切換啟動與暫停()
    {
        var hotkeys = new HotkeyHarness();
        var vm = CreateVmWithStartup(new NoOpStartupService(), hotkeys.Service); // autoStart=false → Paused

        hotkeys.Service.SimulatePress(MainViewModel.ToggleHotkeyId);
        Assert.NotEqual(MonitorStatus.Paused, vm.Status);

        hotkeys.Service.SimulatePress(MainViewModel.ToggleHotkeyId);
        Assert.Equal(MonitorStatus.Paused, vm.Status);
    }

    private sealed class FakeCursorService : CursorService
    {
        public bool ApplyResult = true;
        public bool RestoreResult = true;
        public List<string> Applied { get; } = new();
        public int Restored;

        public FakeCursorService()
            : base(_ => IntPtr.Zero, _ => false, () => false, _ => { },
                Path.Combine(Path.GetTempPath(), "MousePilotFakeMarker", Guid.NewGuid().ToString("N"), "m"))
        {
        }

        public override bool Apply(string curFilePath)
        {
            Applied.Add(curFilePath);
            if (ApplyResult) { IsApplied = true; }
            return ApplyResult;
        }

        public override bool Restore()
        {
            Restored++;
            if (RestoreResult) { IsApplied = false; }
            return RestoreResult;
        }
    }

    [Fact]
    public void 套用游標成功更新狀態與設定()
    {
        var cursor = new FakeCursorService();
        var vm = CreateVmWithStartup(new NoOpStartupService(), cursorService: cursor);
        vm.Settings.ConfirmedCursorFile = @"C:\appdata\confirmed-cursor.cur";
        vm.ApplyCursorCommand.NotifyCanExecuteChanged();

        Assert.True(vm.ApplyCursorCommand.CanExecute(null));
        vm.ApplyCursorCommand.Execute(null);

        Assert.Equal(new[] { @"C:\appdata\confirmed-cursor.cur" }, cursor.Applied);
        Assert.True(vm.Settings.CustomCursorEnabled);
        Assert.Equal("已套用自訂游標", vm.CursorStatusText);
    }

    [Fact]
    public void 套用失敗提示且不改設定()
    {
        var cursor = new FakeCursorService { ApplyResult = false };
        var vm = CreateVmWithStartup(new NoOpStartupService(), cursorService: cursor);
        vm.Settings.ConfirmedCursorFile = @"C:\appdata\confirmed-cursor.cur";

        vm.ApplyCursorCommand.Execute(null);

        Assert.False(vm.Settings.CustomCursorEnabled);
        Assert.Equal("Windows 預設", vm.CursorStatusText);
        Assert.Contains("失敗", vm.Notice);
    }

    [Fact]
    public void 恢復游標更新狀態與設定()
    {
        var cursor = new FakeCursorService();
        var vm = CreateVmWithStartup(new NoOpStartupService(), cursorService: cursor);
        vm.Settings.ConfirmedCursorFile = @"C:\a.cur";
        vm.ApplyCursorCommand.Execute(null);

        vm.RestoreCursorCommand.Execute(null);

        Assert.Equal(1, cursor.Restored);
        Assert.False(vm.Settings.CustomCursorEnabled);
        Assert.Equal("Windows 預設", vm.CursorStatusText);
    }

    [Fact]
    public void F10快捷鍵恢復游標()
    {
        var cursor = new FakeCursorService();
        var hotkeys = new HotkeyHarness();
        var vm = CreateVmWithStartup(new NoOpStartupService(), hotkeys.Service, cursorService: cursor);

        hotkeys.Service.SimulatePress(MainViewModel.RestoreCursorHotkeyId); // = 2

        Assert.Equal(1, cursor.Restored);
        Assert.Contains("已恢復", vm.Notice);
    }

    [Fact]
    public void 編輯器套用請求後自動套用()
    {
        var cursor = new FakeCursorService();
        var vm = CreateVmWithStartup(new NoOpStartupService(), cursorService: cursor);
        vm.AttachCursorEditorLauncher(editorVm =>
        {
            editorVm.SelectedSource = editorVm.Sources.First(s => s.Source.Id == "preset:Heart");
            editorVm.ConfirmAndApplyCommand.Execute(null);
            return true;
        });

        vm.EditCursorCommand.Execute(null);

        Assert.Single(cursor.Applied); // 確認落地路徑（test writer）被套用
        Assert.True(vm.Settings.CustomCursorEnabled);
    }

    private sealed class FakeCursorImportService : CursorImportService
    {
        public CursorImportResult NextResult = new(true, @"C:\store\cat.png", null, 32, 32);
        public List<string> Imported { get; } = new();
        public List<string> Removed { get; } = new();
        public bool RemoveResult = true;

        public FakeCursorImportService() : base(@"C:\store") { }

        public override CursorImportResult Import(string sourcePath)
        {
            Imported.Add(sourcePath);
            return NextResult;
        }

        public override bool Remove(string storedPath)
        {
            Removed.Add(storedPath);
            return RemoveResult;
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
            () => pickedFile,
            cursorService: new FakeCursorService(), // 預設 fake：絕不觸真實 Win32 游標 API
            confirmedCurWriter: _ => Path.Combine(_dir, "confirmed-cursor.cur"));
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
    public void 移除失敗時保留設定並提示()
    {
        var cursor = new FakeCursorImportService { RemoveResult = false };
        var vm = CreateVmWithCursor(cursor, @"C:\pics\cat.png");
        vm.ImportCursorCommand.Execute(null);

        vm.RemoveCursorCommand.Execute(null);

        Assert.Equal(@"C:\store\cat.png", vm.Settings.CursorFile); // 不清設定（Phase 7 移交修復）
        Assert.Contains("無法刪除", vm.Notice);
        Assert.True(vm.RemoveCursorCommand.CanExecute(null));
    }

    [Fact]
    public void 移除圖片後套用按鈕停用()
    {
        var cursor = new FakeCursorImportService();
        var vm = CreateVmWithCursor(cursor, @"C:\pics\cat.png");
        vm.ImportCursorCommand.Execute(null);
        vm.Settings.ConfirmedCursorFile = @"C:\appdata\confirmed-cursor.cur";
        Assert.True(vm.ApplyCursorCommand.CanExecute(null));

        vm.RemoveCursorCommand.Execute(null);

        Assert.Equal("", vm.Settings.ConfirmedCursorFile); // 移除後回到「未確認」（final review 修正）
        Assert.False(vm.ApplyCursorCommand.CanExecute(null));
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
}
