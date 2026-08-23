using System.IO;
using MousePilot.Native;

namespace MousePilot.Services;

/// <summary>
/// 全域游標套用/恢復（規格 §11、Spike B）。
/// 恢復優先：SPI_SETCURSORS 重載使用者 Cursor Scheme，永不保存/寫回個別游標——不可能破壞使用者原始設定。
/// marker 檔＝「已套用未恢復」：Apply 成功寫入、Restore 成功刪除；啟動時若存在代表上次未正常恢復（crash），先補救。
/// </summary>
public class CursorService : IDisposable
{
    private readonly Func<string, IntPtr> _loadCursorFromFile;
    private readonly Func<IntPtr, bool> _setSystemCursor;
    private readonly Func<bool> _reloadScheme;
    private readonly Action<IntPtr> _destroyCursor;
    private readonly string _markerPath;

    public CursorService(
        Func<string, IntPtr>? loadCursorFromFile = null,
        Func<IntPtr, bool>? setSystemCursor = null,
        Func<bool>? reloadScheme = null,
        Action<IntPtr>? destroyCursor = null,
        string? markerPath = null)
    {
        _loadCursorFromFile = loadCursorFromFile ?? NativeMethods.LoadCursorFromFile;
        _setSystemCursor = setSystemCursor ?? (h => NativeMethods.SetSystemCursor(h, NativeMethods.OCR_NORMAL));
        _reloadScheme = reloadScheme ?? NativeMethods.ReloadCursorScheme;
        _destroyCursor = destroyCursor ?? (h => NativeMethods.DestroyCursor(h));
        _markerPath = markerPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MousePilot", "cursor-applied.marker");
    }

    public virtual bool IsApplied { get; protected set; }

    /// <summary>marker 存在＝上次套用後未恢復（crash/強制結束）。</summary>
    public bool HasPendingRestore => File.Exists(_markerPath);

    public virtual bool Apply(string curFilePath)
    {
        if (!File.Exists(curFilePath))
        {
            return false;
        }

        var handle = _loadCursorFromFile(curFilePath);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        // SetSystemCursor 接管並銷毀傳入 handle（Spike B）；失敗時必須自行銷毀避免洩漏
        if (!_setSystemCursor(handle))
        {
            _destroyCursor(handle);
            return false;
        }

        WriteMarker();
        IsApplied = true;
        return true;
    }

    public virtual bool Restore()
    {
        if (!_reloadScheme())
        {
            return false; // 失敗保留 marker，下次啟動補救
        }

        DeleteMarker();
        IsApplied = false;
        return true;
    }

    public void Dispose()
    {
        if (IsApplied)
        {
            Restore();
        }
    }

    private void WriteMarker()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
            File.WriteAllText(_markerPath, "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // marker 寫入失敗不阻止套用（僅失去 crash 補救能力）
        }
    }

    private void DeleteMarker()
    {
        try
        {
            if (File.Exists(_markerPath))
            {
                File.Delete(_markerPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
