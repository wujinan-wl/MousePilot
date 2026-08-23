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
    private readonly CursorService _cursorService;
    private readonly Func<byte[], string?>? _confirmedCurWriter;
    private readonly LogService? _log;
    private Func<CursorEditorViewModel, bool?>? _cursorEditorLauncher;
    private CancellationTokenSource? _moveCts;
    private bool _moving;
    private bool _movingFromAutoCycle;
    private int _consecutiveMoveFailures;
    private bool _errorLatched;

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
        Func<string?>? cursorFilePicker = null,
        Func<CursorEditorViewModel, bool?>? cursorEditorLauncher = null,
        CursorService? cursorService = null,
        Func<byte[], string?>? confirmedCurWriter = null,
        LogService? logService = null)
    {
        _cursorEditorLauncher = cursorEditorLauncher;
        _cursorService = cursorService ?? new CursorService();
        _confirmedCurWriter = confirmedCurWriter;
        _log = logService;
        _settingsService = settingsService;
        var result = settingsService.Load();
        Settings = result.Settings;
        if (result.WasCorrupt)
        {
            Notice = result.BackupPath is null
                ? "設定檔損毀，已載入預設值。"
                : $"設定檔損毀，已載入預設值（原檔備份：{result.BackupPath}）。";
            _log?.Error("設定檔損毀，已載入預設值");
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
        RefreshCursorStatusText();

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

        Status = _errorLatched ? MonitorStatus.Error : result.State;
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
            _log?.Info("移動請求被忽略（前一次尚未完成，防重入）。");
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

            var result = await _movementService.ExecuteMoveAsync(cts.Token);
            switch (result)
            {
                case MoveResult.Success:
                    _consecutiveMoveFailures = 0;
                    _errorLatched = false;
                    break;
                case MoveResult.ConservativeAbort:
                    _log?.Info("滑鼠移動保守放棄（可能偵測到使用者操作，不視為錯誤）。");
                    break;
                case MoveResult.Win32Failure:
                    _consecutiveMoveFailures++;
                    _log?.Error($"滑鼠移動失敗（Win32 呼叫失敗，連續 {_consecutiveMoveFailures} 次）");
                    if (_consecutiveMoveFailures >= 3)
                    {
                        _errorLatched = true;
                    }

                    break;
                case MoveResult.Cancelled:
                default:
                    break; // 使用者操作取消，不視為錯誤，不記錄
            }

            if (_errorLatched)
            {
                Status = MonitorStatus.Error;
            }
        }
        catch (Exception ex)
        {
            Notice = $"滑鼠移動失敗：{ex.Message}";
            _log?.Error("滑鼠移動發生未預期例外", ex);
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
            MonitorStatus.Error => "錯誤（滑鼠移動失敗）",
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

    /// <summary>開始閒置秒數包裝屬性（TextBox 直綁；StartMonitoring 的 Clamp 後刷新）。</summary>
    public int IdleStartSecondsInput
    {
        get => Settings.IdleStartSeconds;
        set
        {
            Settings.IdleStartSeconds = value;
            OnPropertyChanged();
        }
    }

    /// <summary>後續移動間隔秒數包裝屬性（TextBox 直綁；StartMonitoring 的 Clamp 後刷新）。</summary>
    public int MovementIntervalSecondsInput
    {
        get => Settings.MovementIntervalSeconds;
        set
        {
            Settings.MovementIntervalSeconds = value;
            OnPropertyChanged();
        }
    }

    /// <summary>移動像素包裝屬性（TextBox 直綁；StartMonitoring 的 Clamp 後刷新）。</summary>
    public int MovementPixelsInput
    {
        get => Settings.MovementPixels;
        set
        {
            Settings.MovementPixels = value;
            OnPropertyChanged();
        }
    }

    /// <summary>啟動/暫停快捷鍵（規格 §17）。</summary>
    public string ToggleHotkeyText
    {
        get => Settings.ToggleHotkey;
        set => TrySetHotkey(value, isToggle: true);
    }

    /// <summary>恢復 Windows 游標快捷鍵。</summary>
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
        if (!_cursorImportService.Remove(Settings.CursorFile))
        {
            Notice = "無法刪除游標檔案（可能被占用），設定未變更。"; // Phase 7 移交修復：失敗不清設定防孤兒
            return;
        }

        Settings.CursorFile = "";
        Settings.ConfirmedCursorFile = ""; // 移除後回到「未確認」——否則仍可套用已移除的游標（final review 修正）
        _lastImportSize = (null, null);
        RefreshCursorFileText();
        RemoveCursorCommand.NotifyCanExecuteChanged();
        ApplyCursorCommand.NotifyCanExecuteChanged();
    }

    private bool CanRemoveCursor() => Settings.CursorFile.Length > 0;

    [RelayCommand(CanExecute = nameof(CanApplyCursor))]
    private void ApplyCursor()
    {
        if (_cursorService.Apply(Settings.ConfirmedCursorFile))
        {
            Settings.CustomCursorEnabled = true;
            SaveSettings();
            RefreshCursorStatusText();
            Notice = "已套用自訂游標。";
            _log?.Info("已套用自訂游標。");
        }
        else
        {
            Settings.CustomCursorEnabled = false; // 失敗不得留 true——否則下次啟動 auto-apply 每次重試（backlog k）
            SaveSettings();
            Notice = "套用游標失敗（檔案可能已損毀或被移除）。";
            _log?.Error(Notice);
        }
    }

    private bool CanApplyCursor() => Settings.ConfirmedCursorFile.Length > 0;

    [RelayCommand]
    private void RestoreCursor()
    {
        if (_cursorService.Restore())
        {
            Settings.CustomCursorEnabled = false;
            SaveSettings();
            RefreshCursorStatusText();
            Notice = "已恢復 Windows 游標。";
            _log?.Info("已恢復 Windows 游標。");
        }
        else
        {
            Notice = "恢復游標失敗，將於下次啟動自動補救。";
            _log?.Error(Notice);
        }
    }

    /// <summary>系統事件（如 SessionEnding）已直接恢復游標時呼叫：只刷新顯示狀態，
    /// 不動 <see cref="Models.AppSettings.CustomCursorEnabled"/>——下次登入沿用原設定自動套回（移交 i）。</summary>
    public void NotifySystemRestored() => RefreshCursorStatusText();

    private void RefreshCursorStatusText()
        => CursorStatusText = _cursorService.IsApplied ? "已套用自訂游標" : "Windows 預設";

    private (int? Width, int? Height) _lastImportSize;

    /// <summary>View 於 Loaded 時注入編輯視窗啟動器（測試直接注入 fake）。</summary>
    public void AttachCursorEditorLauncher(Func<CursorEditorViewModel, bool?> launcher)
        => _cursorEditorLauncher = launcher;

    [RelayCommand]
    private void EditCursor()
    {
        if (_cursorEditorLauncher is null)
        {
            return; // 尚未接上 View（防呆，不當機）
        }

        var editorVm = new CursorEditorViewModel(Settings, confirmedWriter: _confirmedCurWriter);
        if (_cursorEditorLauncher(editorVm) == true)
        {
            SaveSettings();
            _lastImportSize = (null, null);
            RefreshCursorFileText();
            RemoveCursorCommand.NotifyCanExecuteChanged();
            ApplyCursorCommand.NotifyCanExecuteChanged();
            if (editorVm.ApplyRequested)
            {
                ApplyCursor();
            }
        }
    }

    private void RefreshCursorFileText()
    {
        if (Settings.CursorPreset.Length > 0)
        {
            var preset = CursorGallery.Presets.FirstOrDefault(p => p.Id == Settings.CursorPreset);
            CursorFileText = $"{preset?.DisplayName ?? Settings.CursorPreset}（{Settings.CursorSize} px）";
            return;
        }

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
        if (error is null && HotkeyParser.Parse(text) is { } parsed && HotkeyParser.Parse(other) is { } otherParsed
            && parsed.Equals(otherParsed))
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
            _log?.Error(Notice);
            return;
        }

        if (!_hotkeyService.Register(id, combo))
        {
            var code = _hotkeyService.LastWin32Error;
            Notice = code == 1409
                ? $"快捷鍵 {text} 已被其他程式占用，{label} 快捷鍵未啟用。"
                : $"快捷鍵 {text} 註冊失敗（Win32 錯誤 {code}），{label} 快捷鍵未啟用。";
            _log?.Error(Notice);
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
            RestoreCursor();
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
            // 保存失敗不可讓程式 crash（規格 §21）
            Notice = $"設定保存失敗：{ex.Message}";
            _log?.Error("設定保存失敗", ex);
        }
    }

    private void StartMonitoring()
    {
        _errorLatched = false;
        _consecutiveMoveFailures = 0;
        Settings.Clamp();
        OnPropertyChanged(nameof(IdleStartSecondsInput));
        OnPropertyChanged(nameof(MovementIntervalSecondsInput));
        OnPropertyChanged(nameof(MovementPixelsInput));
        IdleService.Start();
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start() => StartMonitoring();

    private bool CanStart() => Status == MonitorStatus.Paused;

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause()
    {
        _errorLatched = false; // 暫停即解除錯誤 latch——否則 Status 永遠 Error、CanStart 永久 false（review 修正）
        _consecutiveMoveFailures = 0;
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
