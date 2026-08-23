namespace MousePilot.Models;

public enum MovementMode
{
    Horizontal,
    Vertical,
    Random,
}

/// <summary>
/// 應用程式設定。JSON 欄位名稱（camelCase）與範圍依規格 §18。
/// </summary>
public class AppSettings
{
    public static readonly int[] AllowedCursorSizes = { 16, 24, 32, 48, 64, 96, 128 };

    public int IdleStartSeconds { get; set; } = 120;
    public int MovementIntervalSeconds { get; set; } = 30;
    public int MovementPixels { get; set; } = 3;
    public MovementMode MovementMode { get; set; } = MovementMode.Random;
    public bool ReturnToOriginalPosition { get; set; } = true;
    public bool RunAtStartup { get; set; }
    public bool StartMinimized { get; set; } = true;
    public bool AutoStartMonitoring { get; set; } = true;
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool CustomCursorEnabled { get; set; }
    public string CursorFile { get; set; } = "";
    public string CursorPreset { get; set; } = "";
    public List<string> FavoriteCursors { get; set; } = new();
    public int CursorSize { get; set; } = 32;
    public int CursorHotspotX { get; set; }
    public int CursorHotspotY { get; set; }
    public string ToggleHotkey { get; set; } = "Ctrl+Alt+F9";
    public string RestoreCursorHotkey { get; set; } = "Ctrl+Alt+F10";

    /// <summary>「確定」時落地的實際套用檔（processed .cur 或原 .cur/.ani 路徑）；Phase 9 套用一律讀此欄。</summary>
    public string ConfirmedCursorFile { get; set; } = "";

    /// <summary>把所有數值夾制到規格允許範圍（載入與保存前呼叫）。</summary>
    public void Clamp()
    {
        // 合法 JSON 的 null 欄位會反序列化為 null（不觸發損毀備份路徑）——正規化防啟動 NRE（review 修正）
        CursorFile ??= "";
        CursorPreset ??= "";
        ToggleHotkey ??= "Ctrl+Alt+F9";
        RestoreCursorHotkey ??= "Ctrl+Alt+F10";
        FavoriteCursors ??= new();
        ConfirmedCursorFile ??= "";

        IdleStartSeconds = Math.Clamp(IdleStartSeconds, 5, 86400);
        MovementIntervalSeconds = Math.Clamp(MovementIntervalSeconds, 1, 86400);
        MovementPixels = Math.Clamp(MovementPixels, 1, 100);
        if (!AllowedCursorSizes.Contains(CursorSize))
        {
            CursorSize = 32;
        }

        // 移交 (h)：手改 settings.json 防護——hotspot 夾制於尺寸範圍；preset/file 互斥（preset 優先，與 RefreshCursorFileText 一致）
        CursorHotspotX = Math.Clamp(CursorHotspotX, 0, CursorSize - 1);
        CursorHotspotY = Math.Clamp(CursorHotspotY, 0, CursorSize - 1);
        if (CursorPreset.Length > 0 && CursorFile.Length > 0)
        {
            CursorFile = "";
        }
    }
}
