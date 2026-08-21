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
    public const int ToggleHotkeyId = 1;
    public const int RestoreCursorHotkeyId = 2;

    private readonly SettingsService _settingsService;
    private readonly MouseMovementService _movementService;
    private readonly StartupService _startupService;
    private readonly HotkeyService _hotkeyService;
    private readonly CursorImportService _cursorImportService;
    private readonly Func<string?> _cursorFilePicker;
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

    [ObservableProperty]
    private string _cursorFileText = "未選擇";

    public MainViewModel(
        SettingsService settingsService,
        Func<AppSettings, IdleDetectionService>? idleServiceFactory = null,
        Func<AppSettings, IdleDetectionService, MouseMovementService>? movementServiceFactory = null,
        StartupService? startupService = null,
        HotkeyService? hotkeyService = null,
        CursorImportService? cursorImportService = null,
        Func<string?>? cursorFilePicker = null)
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

        _hotkeyService = hotkeyService ?? new HotkeyService();
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
        RegisterHotkeyFromSettings(ToggleHotkeyId, Settings.ToggleHotkey, "啟動/暫停");
        RegisterHotkeyFromSettings(RestoreCursorHotkeyId, Settings.RestoreCursorHotkey, "恢復游標");

        _cursorImportService = cursorImportService ?? new CursorImportService();
        _cursorFilePicker = cursorFilePicker ?? PickCursorFile;
        RefreshCursorFileText();

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

    /// <summary>啟動/暫停快捷鍵（規格 §17）。</summary>
    public string ToggleHotkeyText
    {
        get => Settings.ToggleHotkey;
        set => TrySetHotkey(value, isToggle: true);
    }

    /// <summary>恢復 Windows 游標快捷鍵（動作於 Phase 9 接上）。</summary>
    public string RestoreCursorHotkeyText
    {
        get => Settings.RestoreCursorHotkey;
        set => TrySetHotkey(value, isToggle: false);
    }

    /// <summary>匯入游標圖片（規格 §7/§8；套用到 Windows 全域為 Phase 9）。</summary>
    [RelayCommand]
    private void ImportCursor()
    {
        if (_cursorFilePicker() is not { } path)
        {
            return; // 使用者取消選檔
        }

        var result = _cursorImportService.Import(path);
        if (!result.Success)
        {
            Notice = result.Error ?? "匯入失敗。";
            return;
        }

        Settings.CursorFile = result.StoredPath!;
        Settings.CursorPreset = "";
        _lastImportSize = (result.Width, result.Height);
        RefreshCursorFileText();
        RemoveCursorCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRemoveCursor))]
    private void RemoveCursor()
    {
        _cursorImportService.Remove(Settings.CursorFile);
        Settings.CursorFile = "";
        _lastImportSize = (null, null);
        RefreshCursorFileText();
        RemoveCursorCommand.NotifyCanExecuteChanged();
    }

    private bool CanRemoveCursor() => Settings.CursorFile.Length > 0;

    private (int? Width, int? Height) _lastImportSize;

    private void RefreshCursorFileText()
    {
        if (Settings.CursorFile.Length == 0)
        {
            CursorFileText = "未選擇";
            return;
        }

        var name = System.IO.Path.GetFileName(Settings.CursorFile);
        CursorFileText = _lastImportSize.Width is { } w && _lastImportSize.Height is { } h
            ? $"{name}（{w}x{h}）"
            : name;
    }

    private static string? PickCursorFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "選擇游標圖片",
            Filter = "游標與圖片|*.png;*.jpg;*.jpeg;*.bmp;*.cur;*.ani|所有檔案|*.*",
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private void TrySetHotkey(string text, bool isToggle)
    {
        var current = isToggle ? Settings.ToggleHotkey : Settings.RestoreCursorHotkey;
        var propertyName = isToggle ? nameof(ToggleHotkeyText) : nameof(RestoreCursorHotkeyText);
        if (text == current)
        {
            return;
        }

        var error = HotkeyParser.Validate(text);
        var other = isToggle ? Settings.RestoreCursorHotkey : Settings.ToggleHotkey;
        if (error is null && text == other)
        {
            error = "快捷鍵不可與另一項重複。";
        }

        if (error is null)
        {
            var id = isToggle ? ToggleHotkeyId : RestoreCursorHotkeyId;
            var combo = HotkeyParser.Parse(text)!.Value;
            if (_hotkeyService.Register(id, combo))
            {
                if (isToggle)
                {
                    Settings.ToggleHotkey = text;
                }
                else
                {
                    Settings.RestoreCursorHotkey = text;
                }
            }
            else
            {
                error = $"快捷鍵 {text} 已被其他程式占用。";
                if (HotkeyParser.Parse(current) is { } old)
                {
                    _hotkeyService.Register(id, old); // 還原舊組合的註冊
                }
            }
        }

        if (error is not null)
        {
            Notice = error;
        }

        OnPropertyChanged(propertyName);
    }

    private void RegisterHotkeyFromSettings(int id, string text, string label)
    {
        if (HotkeyParser.Parse(text) is not { } combo)
        {
            Notice = $"快捷鍵設定「{text}」無效，{label} 快捷鍵未啟用。";
            return;
        }

        if (!_hotkeyService.Register(id, combo))
        {
            Notice = $"快捷鍵 {text} 已被其他程式占用，{label} 快捷鍵未啟用。";
        }
    }

    private void OnHotkeyPressed(int id)
    {
        if (id == ToggleHotkeyId)
        {
            if (Status == MonitorStatus.Paused)
            {
                if (StartCommand.CanExecute(null))
                {
                    StartCommand.Execute(null);
                }
            }
            else if (PauseCommand.CanExecute(null))
            {
                PauseCommand.Execute(null);
            }
        }
        else if (id == RestoreCursorHotkeyId)
        {
            Notice = "恢復 Windows 游標功能將於自訂游標功能完成後啟用。";
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
        _moveCts?.Cancel();        // §30：結束時取消所有背景動作（含 300ms 返回等待）
        _hotkeyService.Dispose();  // §30 步驟 4：Unregister Global Hotkey（刻意先於步驟 3 的 timer 停止——先解除輸入觸發源）
        IdleService.Dispose();
    }
}
