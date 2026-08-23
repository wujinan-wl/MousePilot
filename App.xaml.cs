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
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

        // 未處理例外最小 hook（Phase 11 才有完整 handler）：只恢復游標，不吞例外
        DispatcherUnhandledException += (_, _) => _cursorService?.Restore();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => _cursorService?.Restore();
        SessionEnding += (_, _) => _cursorService?.Restore(); // Windows 登出/關機

        var vm = new MainViewModel(new SettingsService(), cursorService: _cursorService); // 區域變數供 lambda 捕捉（避免 nullable 欄位的 CS8602 警告）
        _mainViewModel = vm;
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        window.Closing += OnMainWindowClosing;

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

        _singleInstance.WakeRequested += () => Dispatcher.Invoke(ShowDashboard); // threadpool → UI thread

        if (vm.Settings.CustomCursorEnabled && vm.ApplyCursorCommand.CanExecute(null))
        {
            vm.ApplyCursorCommand.Execute(null); // 延續上次套用狀態（計畫決策 3）
        }

        if (!vm.Settings.StartMinimized)
        {
            window.Show();
        }
        // StartMinimized=true（規格 §16 預設）：不顯示視窗，僅系統匣
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Status) or nameof(MainViewModel.StatusText)
            && _mainViewModel is not null)
        {
            _tray?.UpdateStatus(_mainViewModel.Status, _mainViewModel.StatusText);
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
        Shutdown();                     // 9：關閉程式
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 保險路徑：非 Tray 結束（例外等）也要保存與釋放
        if (!_exiting)
        {
            _mainViewModel?.SaveSettings();
            _mainViewModel?.Dispose();
            _tray?.Dispose();
            _cursorService?.Dispose();
            _singleInstance?.Dispose();
        }

        base.OnExit(e);
    }
}
