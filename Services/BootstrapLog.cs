using System.IO;

namespace MousePilot.Services;

/// <summary>
/// 極早期開機紀錄：LogService 建立前唯一可用的紀錄管道（App 建構子起即可寫）。
/// 主要位置 %AppData%\MousePilot\Logs\mousepilot-bootstrap.log，不可用時退回 %TEMP%；
/// 任何寫入失敗一律靜默——診斷紀錄不得造成第二次例外。
/// </summary>
public static class BootstrapLog
{
    /// <summary>超過此大小改為重寫（crash loop 防護），bootstrap log 不做輪替。</summary>
    public const long MaxBytes = 512 * 1024;

    private const string FileName = "mousepilot-bootstrap.log";

    public static string DefaultPrimaryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MousePilot", "Logs", FileName);

    public static string DefaultFallbackPath => Path.Combine(Path.GetTempPath(), FileName);

    /// <summary>寫入預設位置，回傳實際寫入的路徑；兩路徑都失敗回傳 null（不拋例外）。</summary>
    public static string? Write(string message) => Write(message, DefaultPrimaryPath, DefaultFallbackPath);

    public static string? Write(string message, string primaryPath, string fallbackPath)
        => TryAppend(primaryPath, message) ? primaryPath
         : TryAppend(fallbackPath, message) ? fallbackPath
         : null;

    private static bool TryAppend(string path, string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
            if (new FileInfo(path) is { Exists: true, Length: > MaxBytes })
            {
                File.WriteAllText(path, line);
            }
            else
            {
                File.AppendAllText(path, line);
            }

            return true;
        }
        catch
        {
            return false; // 靜默：唯一合法的裸 catch 情境——診斷紀錄不得反噬
        }
    }
}
