using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MousePilot.Models;
using MousePilot.Services;

namespace MousePilot.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly MouseMovementService _movementService;
    private readonly StartupService _startupService;
    private CancellationTokenSource? _moveCts;
    private bool _moving;
    private bool _movingFromAutoCycle;

    public AppSettings Settings { get; }

    public IdleDetectionService IdleService { get; }

    [ObservableProperty]
    private MonitorStatus _status = MonitorStatus.Paused;

    [ObservableProperty]
    private string _statusText = "已暫停";

    [ObservableProperty]
    private double _idleSeconds;

    [ObservableProperty]
    private string _mousePosition = "—";

    [ObservableProperty]
    private string _cursorStatusText = "Windows 預設";

    [ObservableProperty]
    private string _notice = "";

    [ObservableProperty]
    private string _firstTriggerText = "—";

    [ObservableProperty]
    private string _nextMoveText = "—";

    [ObservableProperty]
    private int _triggerCount;

    public MainViewModel(
        SettingsService settingsService,
        Func<AppSettings, IdleDetectionService>? idleServiceFactory = null,
        Func<AppSettings, IdleDetectionService, MouseMovementService>? movementServiceFactory = null,
        StartupService? startupService = null)
    {
        _settingsService = settingsService;
        var result = settingsService.Load();
        Settings = result.Settings;
        if (result.WasCorrupt)
        {
            Notice = result.BackupPath is null
                ? "設定檔損毀，已載入預設值。"
                : $"設定檔損毀，已載入預設值（原檔備份：{result.BackupPath}）。";
        }

        IdleService = (idleServiceFactory ?? (s => new IdleDetectionService(s)))(Settings);
        _movementService = (movementServiceFactory ?? ((s, i) => new MouseMovementService(s, i)))(Settings, IdleService);

        _startupService = startupService ?? new StartupService();
        // Registry 為此設定的真實來源：回填實際狀態；已註冊則冪等重寫目前 EXE 路徑（修復移動後失效）
        if (_startupService.IsEnabled() is { } registered)
        {
            Settings.RunAtStartup = registered;
            if (registered)
            {
                if (!_startupService.Enable())
                {
                    Notice = "開機自動啟動路徑修復失敗，請重新勾選一次。";
                }
            }
        }

        IdleService.Ticked += OnTicked;
        IdleService.MoveRequested += OnMoveRequested;

        if (Settings.AutoStartMonitoring)
        {
            StartMonitoring();
        }
    }

    private void OnTicked(IdleTickResult result, (int X, int Y)? cursor)
    {
        if (result.State == MonitorStatus.UserActive && _movingFromAutoCycle)
        {
            // 真實使用者輸入→取消自動週期中的移動（規格 §24）。
            // 手動「立即執行一次」不在此取消：使用者剛點過按鈕必為 UserActive，
            // 誤取消會讓返回失敗；手動移動由服務內返回前雙重防線保護（final review Issue 1）。
            _moveCts?.Cancel();
        }

        Status = result.State;
        IdleSeconds = result.IdleSeconds;
        MousePosition = cursor is { } c ? $"X={c.X}, Y={c.Y}" : "—";
        FirstTriggerText = result.SecondsUntilFirstTrigger is { } f ? $"{f:F0} 秒" : "—";
        NextMoveText = result.SecondsUntilNextMove is { } n ? $"{n:F0} 秒" : "—";
    }

    // 訂閱端不得拋例外（Phase 2 移交約束 2）：整個移動流程包在 guarded 方法內
    private async void OnMoveRequested() => await ExecuteMoveGuardedAsync(fromAutoCycle: true);

    /// <summary>立即執行一次（規格 §14）：不等 Idle Timer，依目前設定執行。</summary>
    [RelayCommand]
    private async Task MoveOnceAsync() => await ExecuteMoveGuardedAsync(fromAutoCycle: false);

    private async Task ExecuteMoveGuardedAsync(bool fromAutoCycle)
    {
        if (_moving)
        {
            return; // 防重入：自動觸發與手動執行不重疊
        }

        _moving = true;
        _movingFromAutoCycle = fromAutoCycle;
        var cts = new CancellationTokenSource();
        _moveCts = cts;
        try
        {
            if (fromAutoCycle)
            {
                TriggerCount++;
                IdleService.BeginMove();
            }

            await _movementService.ExecuteMoveAsync(cts.Token);
        }
        catch (Exception ex)
        {
            Notice = $"滑鼠移動失敗：{ex.Message}"; // Phase 11 接上 LogService 後記錄
        }
        finally
        {
            if (fromAutoCycle)
            {
                IdleService.EndMove();
            }

            _moveCts = null;
            cts.Dispose();
            _movingFromAutoCycle = false;
            _moving = false;
        }
    }

    partial void OnStatusChanged(MonitorStatus value)
    {
        StatusText = value switch
        {
            MonitorStatus.Paused => "已暫停",
            MonitorStatus.Monitoring => "監控中",
            MonitorStatus.UserActive => "使用者活動中",
            MonitorStatus.WaitingToStart => "等待啟動",
            MonitorStatus.AutoMoving => "自動移動中",
            _ => value.ToString(),
        };
        StartCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
    }

    /// <summary>開機自啟（規格 §15）：先寫 Registry、成功才更新設定；失敗顯示提示並讓 checkbox 還原。</summary>
    public bool RunAtStartup
    {
        get => Settings.RunAtStartup;
        set
        {
            if (value == Settings.RunAtStartup)
            {
                return;
            }

            var ok = value ? _startupService.Enable() : _startupService.Disable();
            if (ok)
            {
                Settings.RunAtStartup = value;
            }
            else
            {
                Notice = value
                    ? "無法寫入開機自動啟動設定。"
                    : "無法移除開機自動啟動設定。";
            }

            OnPropertyChanged(); // 失敗時 getter 仍回舊值 → checkbox 還原
        }
    }

    public void SaveSettings()
    {
        try
        {
            _settingsService.Save(Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // 保存失敗不可讓程式 crash（規格 §21）；Phase 11 接上 LogService 後記錄
            Notice = $"設定保存失敗：{ex.Message}";
        }
    }

    private void StartMonitoring()
    {
        Settings.Clamp();
        IdleService.Start();
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start() => StartMonitoring();

    private bool CanStart() => Status == MonitorStatus.Paused;

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause()
    {
        _moveCts?.Cancel();
        IdleService.Pause();
    }

    private bool CanPause() => Status != MonitorStatus.Paused;

    public void Dispose()
    {
        _moveCts?.Cancel(); // §30：結束時取消所有背景動作（含 300ms 返回等待）
        IdleService.Dispose();
    }
}
