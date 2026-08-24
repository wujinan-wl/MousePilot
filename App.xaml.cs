using System.ComponentModel;
using System.Windows;
using MousePilot.Services;
using MousePilot.ViewModels;
using MousePilot.Views;

namespace MousePilot;

public partial class App : Application
{
    private MainViewModel? _mainViewModel;
    private TrayIconService? _tray;
    private CursorService? _cursorService;
    private SingleInstanceService? _singleInstance;
    private LogService? _logService;
    private bool _exiting;

    public App()
    {
        // 極早期開機紀錄（診斷用）：App 建構子是受控程式碼第一站——這行有寫出代表 .NET/WPF host 啟動成功，
        // crash 發生在其後；沒寫出則 crash 在原生初始化（driver/host 層）。寫入失敗靜默。
        BootTrace("App ctor");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            BootTrace($"AppDomain 例外：{args.ExceptionObject}");
        DispatcherUnhandledException += (_, args) => BootTrace($"Dispatcher 例外：{args.Exception}");
    }

    private static void BootTrace(string message)
    {
        try
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mousepilot-boot.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // 診斷紀錄不得反噬
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        BootTrace("OnStartup 進入");
        base.OnStartup(e);
        try
        {
            StartupCore(e);
        }
        catch (Exception ex)
        {
            // 啟動期任何未預期例外：確保留下紀錄並讓使用者看得到（Win10 實機曾發生早期 crash 無任何線索）
            (_logService ?? new LogService()).Error("啟動失敗（OnStartup）", ex);
            try { _cursorService?.Restore(); } catch { /* 最後防線內不得再拋 */ }
            MessageBox.Show($"MousePilot 啟動失敗：\n{ex}", "MousePilot", MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }

    private void StartupCore(StartupEventArgs e)
    {
        _cursorService = new CursorService();

        if (e.Args.Contains("--restore-cursor"))
        {
            _cursorService.Restore(); // 緊急補救參數（Spike B --restore-only 語意）：恢復後直接結束
            Shutdown();
            return;
        }

        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.TryAcquire())
        {
            _singleInstance.SignalFirstInstance(); // 通知原實例開啟 Dashboard（規格 §20）
            Shutdown();
            return; // 第二實例：零副作用讓路（不補救 marker、不掛 hooks、不建 VM）
        }

        if (_cursorService.HasPendingRestore)
        {
            _cursorService.Restore(); // 上次未正常恢復（crash）——先補救再繼續啟動
        }

        _logService = new LogService(); // mutex 取得之後才建——第二實例不記 log
        _logService.Info("啟動階段：單一實例通過");

        if (_singleInstance.AcquiredViaFailOpen)
        {
            _logService.Error("單一實例 mutex 建立失敗（kernel object 名稱被占用或拒絕），以 fail-open 模式啟動——本次執行失去單一實例保證");
        }

        DispatcherUnhandledException += (_, args) => EmergencyShutdown("Dispatcher", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => EmergencyShutdown("AppDomain", args.ExceptionObject as Exception);
        SessionEnding += (_, _) =>
        {
            _logService?.Info("Session 結束（登出/關機）——恢復游標");
            _cursorService?.Restore();
            _mainViewModel?.NotifySystemRestored();
        };

        var vm = new MainViewModel(new SettingsService(), cursorService: _cursorService, logService: _logService); // 區域變數供 lambda 捕捉（避免 nullable 欄位的 CS8602 警告）
        _mainViewModel = vm;
        _logService.Info("啟動階段：服務初始化完成");
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        window.Closing += OnMainWindowClosing;
        _logService.Info("啟動階段：主視窗建立完成");

        _tray = new TrayIconService();
        _tray.OpenRequested += ShowDashboard;
        _tray.StartRequested += () => { if (vm.StartCommand.CanExecute(null)) { vm.StartCommand.Execute(null); } };
        _tray.PauseRequested += () => { if (vm.PauseCommand.CanExecute(null)) { vm.PauseCommand.Execute(null); } };
        _tray.MoveOnceRequested += () => vm.MoveOnceCommand.Execute(null);
        _tray.ExitRequested += ExitApplication;
        _tray.EnableCursorRequested += () => { if (vm.ApplyCursorCommand.CanExecute(null)) { vm.ApplyCursorCommand.Execute(null); } };
        _tray.DisableCursorRequested += () => vm.RestoreCursorCommand.Execute(null);
        _tray.RestoreCursorRequested += () => vm.RestoreCursorCommand.Execute(null);
        vm.PropertyChanged += OnViewModelPropertyChanged;
        _tray.UpdateStatus(vm.Status, vm.StatusText);
        _logService.Info("啟動階段：系統匣就緒");

        _singleInstance.WakeRequested += () => Dispatcher.BeginInvoke(ShowDashboard); // BeginInvoke：dispatcher 關閉時靜默 abort，不丟例外（review 修正）
        _singleInstance.StartListening(); // 訂閱後才開始監聽——關閉啟動期喚醒丟失窗（review 修正）

        if (vm.Settings.CustomCursorEnabled && vm.ApplyCursorCommand.CanExecute(null))
        {
            vm.ApplyCursorCommand.Execute(null); // 延續上次套用狀態（計畫決策 3）
        }

        if (!vm.Settings.StartMinimized)
        {
            window.Show();
        }
        // StartMinimized=true（規格 §16 預設）：不顯示視窗，僅系統匣

        _logService.Info("程式啟動");
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Status) or nameof(MainViewModel.StatusText)
            && _mainViewModel is not null)
        {
            _tray?.UpdateStatus(_mainViewModel.Status, _mainViewModel.StatusText);
        }

        if (e.PropertyName == nameof(MainViewModel.Notice)
            && _mainViewModel is { } vmn && vmn.Notice.Length > 0
            && MainWindow?.IsVisible != true)
        {
            _tray?.ShowInfo(vmn.Notice); // tray-only 狀態下的通知可見性（P6 移交 b）
        }
    }

    private void ShowDashboard()
    {
        if (MainWindow is null)
        {
            return;
        }

        MainWindow.Show();
        if (MainWindow.WindowState == WindowState.Minimized)
        {
            MainWindow.WindowState = WindowState.Normal;
        }

        MainWindow.Activate();
    }

    private void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting)
        {
            return;
        }

        if (_mainViewModel?.Settings.MinimizeToTrayOnClose == true)
        {
            e.Cancel = true;       // 關閉 → 縮到系統匣（規格 §13 預設）
            MainWindow?.Hide();
        }
        else
        {
            ExitApplication();     // 設定為真的關閉
        }
    }

    /// <summary>安全結束流程（規格 §30 現階段子集；cursor/mutex 由 Phase 9/10 插入對應步驟）。</summary>
    private void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _mainViewModel?.Dispose();      // 1~4：取消進行中移動、解除快捷鍵、停止輪詢 timer
        _cursorService?.Dispose();      // 5：恢復游標（已套用才動作）
        _tray?.Dispose();               // 6：系統匣圖示
        _mainViewModel?.SaveSettings(); // 7：保存設定
        _singleInstance?.Dispose();     // 8：釋放 Mutex
        _logService?.Info("程式結束");
        Shutdown();                     // 9：關閉程式
    }

    /// <summary>未處理例外的最後防線（移交 b/e/g）：每步各自 try/catch 到底；不吞例外——清理後讓程序終止。
    /// 移交 (g)：HotkeyService WndProc 例外沿 Dispatcher 傳播，<see cref="Application.DispatcherUnhandledException"/> 已涵蓋。
    /// 順序刻意異於 §30（先存設定後清 tray）：緊急路徑優先保全持久狀態，tray 清理屬外觀性。</summary>
    private void EmergencyShutdown(string source, Exception? ex)
    {
        try { _logService?.Error($"未處理例外（{source}）", ex); } catch { /* 最後防線內不得再拋——專案唯一允許的裸 catch */ }
        try { _cursorService?.Restore(); } catch { }
        try { _mainViewModel?.SaveSettings(); } catch { }
        try { _tray?.Dispose(); } catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 保險路徑：非 Tray 結束（例外等）也要保存與釋放
        if (!_exiting)
        {
            _mainViewModel?.SaveSettings();
            _logService?.Info("程式結束（OnExit 保險路徑）");
            _mainViewModel?.Dispose();
            _tray?.Dispose();
            _cursorService?.Dispose();
            _singleInstance?.Dispose();
        }

        base.OnExit(e);
    }
}
