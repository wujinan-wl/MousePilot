using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MousePilot.Models;
using MousePilot.Services;

namespace MousePilot.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settingsService;

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
        Func<AppSettings, IdleDetectionService>? idleServiceFactory = null)
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
        IdleService.Ticked += OnTicked;
        IdleService.MoveRequested += OnMoveRequested;

        if (Settings.AutoStartMonitoring)
        {
            StartMonitoring();
        }
    }

    private void OnTicked(IdleTickResult result, (int X, int Y)? cursor)
    {
        Status = result.State;
        IdleSeconds = result.IdleSeconds;
        MousePosition = cursor is { } c ? $"X={c.X}, Y={c.Y}" : "—";
        FirstTriggerText = result.SecondsUntilFirstTrigger is { } f ? $"{f:F0} 秒" : "—";
        NextMoveText = result.SecondsUntilNextMove is { } n ? $"{n:F0} 秒" : "—";
    }

    // Phase 3 接上 MouseMovementService 執行實際移動；目前僅累計佔位事件
    private void OnMoveRequested() => TriggerCount++;

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
    private void Pause() => IdleService.Pause();

    private bool CanPause() => Status != MonitorStatus.Paused;

    public void Dispose() => IdleService.Dispose();
}
