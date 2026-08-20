using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MousePilot.Models;
using MousePilot.Services;

namespace MousePilot.ViewModels;

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

    partial void OnStatusChanged(MonitorStatus value) => StatusText = value switch
    {
        MonitorStatus.Paused => "已暫停",
        MonitorStatus.Monitoring => "監控中",
        MonitorStatus.UserActive => "使用者活動中",
        MonitorStatus.WaitingToStart => "等待啟動",
        MonitorStatus.AutoMoving => "自動移動中",
        _ => value.ToString(),
    };

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

    // Phase 2 接上 IdleDetectionService 後改為真實啟停邏輯與 CanExecute 條件
    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start() { }

    private bool CanStart() => false;

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause() { }

    private bool CanPause() => false;
}
