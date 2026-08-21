using System.IO;
using Microsoft.Win32;

namespace MousePilot.Services;

/// <summary>
/// 開機自動啟動（規格 §15）：只讀寫 HKCU 的 Run key，絕不碰 HKLM（不需 Administrator）。
/// 方法為 virtual 供測試偽造失敗；key 路徑可注入供測試用臨時 key。
/// EXE 路徑用 Environment.ProcessPath——PublishSingleFile 下 Assembly.Location 為空，不可用。
/// </summary>
public class StartupService
{
    private readonly string? _exePath;
    private readonly string _runKeyPath;
    private readonly string _valueName;

    public StartupService(
        string? exePath = null,
        string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run",
        string valueName = "MousePilot")
    {
        _exePath = exePath ?? Environment.ProcessPath;
        _runKeyPath = runKeyPath;
        _valueName = valueName;
    }

    /// <summary>寫入（或以目前 EXE 路徑更新）開機自啟。失敗回傳 false，不擲例外（規格 §21）。</summary>
    public virtual bool Enable()
    {
        if (string.IsNullOrEmpty(_exePath))
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(_runKeyPath);
            if (key is null)
            {
                return false;
            }

            key.SetValue(_valueName, $"\"{_exePath}\"");
            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>移除開機自啟。值不存在視為成功；失敗回傳 false，不擲例外。</summary>
    public virtual bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true);
            key?.DeleteValue(_valueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>目前是否已註冊開機自啟；null = 讀取失敗（呼叫端不應據以改動任何狀態）。</summary>
    public virtual bool? IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath);
            return key?.GetValue(_valueName) is string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
