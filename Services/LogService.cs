using System.IO;

namespace MousePilot.Services;

/// <summary>
/// 檔案 log（規格 §29）：mousepilot.log + 輪替歸檔 mousepilot.1.log ~ .N.log（1 最新）。
/// 所有檔案操作自我 try/catch 靜默——log 失敗絕不得影響程式（規格 §21）。執行緒安全。
/// </summary>
public class LogService
{
    private readonly object _gate = new();
    private readonly string _dir;
    private readonly long _maxBytes;
    private readonly int _keepArchives;
    private readonly Func<DateTime> _clock;

    public LogService(string? logDir = null, long maxBytes = 5 * 1024 * 1024, int keepArchives = 3, Func<DateTime>? clock = null)
    {
        _dir = logDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MousePilot", "Logs");
        _maxBytes = maxBytes;
        _keepArchives = keepArchives;
        _clock = clock ?? (() => DateTime.Now);
    }

    public string LogFilePath => Path.Combine(_dir, "mousepilot.log");

    public void Info(string message) => Write("INFO", message);

    public void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message}｜{ex}"); // ex.ToString()：含型別、Message、完整堆疊與 inner exception（啟動 crash 診斷必要）

    private void Write(string level, string message)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_dir);
                RotateIfNeeded();
                File.AppendAllText(LogFilePath,
                    $"{_clock():yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // 靜默：log 不得反噬
            }
        }
    }

    private void RotateIfNeeded()
    {
        var info = new FileInfo(LogFilePath);
        if (!info.Exists || info.Length < _maxBytes)
        {
            return;
        }

        var oldest = ArchivePath(_keepArchives);
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var i = _keepArchives - 1; i >= 1; i--)
        {
            if (File.Exists(ArchivePath(i)))
            {
                File.Move(ArchivePath(i), ArchivePath(i + 1));
            }
        }

        File.Move(LogFilePath, ArchivePath(1));
    }

    private string ArchivePath(int index) => Path.Combine(_dir, $"mousepilot.{index}.log");
}
