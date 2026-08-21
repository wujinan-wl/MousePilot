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
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var vm = new MainViewModel(new SettingsService()); // 區域變數供 lambda 捕捉（避免 nullable 欄位的 CS8602 警告）
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
        vm.PropertyChanged += OnViewModelPropertyChanged;
        _tray.UpdateStatus(vm.Status, vm.StatusText);

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

    /// <summary>安全結束流程（規格 §30 現階段子集；hotkey/cursor/mutex 由 Phase 6/9/10 插入對應步驟）。</summary>
    private void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _mainViewModel?.Dispose();      // 1~3：取消進行中移動、停止輪詢 timer
        _tray?.Dispose();               // 6：系統匣圖示
        _mainViewModel?.SaveSettings(); // 7：保存設定
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
        }

        base.OnExit(e);
    }
}
