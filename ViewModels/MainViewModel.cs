using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MousePilot.Models;
using MousePilot.Services;

namespace MousePilot.ViewModels;

public enum MonitorStatus
{
    Paused,
    Monitoring,
    UserActive,
    WaitingToStart,
    AutoMoving,
}

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;

    public AppSettings Settings { get; }

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

    public MainViewModel(SettingsService settingsService)
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
    }

    public void SaveSettings() => _settingsService.Save(Settings);

    // Phase 2 接上 IdleDetectionService 後改為真實啟停邏輯與 CanExecute 條件
    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start() { }

    private bool CanStart() => false;

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause() { }

    private bool CanPause() => false;
}
