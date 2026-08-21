using System.Drawing;
using System.IO;

namespace MousePilot.Services;

public sealed record CursorImportResult(bool Success, string? StoredPath, string? Error, int? Width, int? Height);

/// <summary>
/// 匯入游標檔案：驗證副檔名、複製到儲存目錄（同名自動編號）、探測尺寸供顯示。
/// 錯誤一律回 Result（規格 §21 不 crash）；ANI 解析失敗優雅降級（仍匯入、僅無預覽尺寸）。
/// </summary>
public class CursorImportService
{
    public static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".cur", ".ani" };

    private readonly string _storageDir;

    public CursorImportService(string? storageDir = null)
    {
        _storageDir = storageDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MousePilot", "Cursors");
    }

    public virtual CursorImportResult Import(string sourcePath)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                return new CursorImportResult(false, null, "找不到檔案。", null, null);
            }

            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (!SupportedExtensions.Contains(ext))
            {
                return new CursorImportResult(false, null, $"不支援的檔案格式（{ext}）。", null, null);
            }

            Directory.CreateDirectory(_storageDir);
            var destPath = UniqueDestPath(Path.GetFileName(sourcePath));
            File.Copy(sourcePath, destPath);

            var (width, height, probeError) = Probe(destPath, ext);
            if (probeError is not null)
            {
                File.Delete(destPath); // 圖片損毀 → 不留殘檔
                return new CursorImportResult(false, null, probeError, null, null);
            }

            return new CursorImportResult(true, destPath, null, width, height);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CursorImportResult(false, null, $"匯入失敗：{ex.Message}", null, null);
        }
    }

    /// <summary>僅允許刪除儲存目錄內的檔案（防止誤刪任意路徑）。</summary>
    public virtual bool Remove(string storedPath)
    {
        try
        {
            var full = Path.GetFullPath(storedPath);
            if (!full.StartsWith(Path.GetFullPath(_storageDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            File.Delete(full);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string UniqueDestPath(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var candidate = Path.Combine(_storageDir, fileName);
        var n = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(_storageDir, $"{name} ({n++}){ext}");
        }

        return candidate;
    }

    private static (int? Width, int? Height, string? Error) Probe(string path, string ext)
    {
        switch (ext)
        {
            case ".cur":
            {
                var read = CurFileFormat.TryReadFirstImage(File.ReadAllBytes(path));
                if (read is null)
                {
                    return (null, null, "CUR 檔案損毀或格式不支援。");
                }

                read.Value.Image.Dispose();
                return (read.Value.Info.Width, read.Value.Info.Height, null);
            }

            case ".ani":
            {
                var read = CurFileFormat.TryReadAniFirstFrame(File.ReadAllBytes(path));
                if (read is null)
                {
                    return (null, null, null); // 優雅降級：僅無預覽，Windows 仍可能載入
                }

                read.Value.Image.Dispose();
                return (read.Value.Info.Width, read.Value.Info.Height, null);
            }

            default:
            {
                try
                {
                    using var bmp = new Bitmap(path);
                    return (bmp.Width, bmp.Height, null);
                }
                catch (ArgumentException)
                {
                    return (null, null, "圖片損毀或格式不支援。");
                }
            }
        }
    }
}
