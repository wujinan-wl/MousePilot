using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MousePilot.Models;

namespace MousePilot.Services;

public sealed class SettingsLoadResult
{
    public required AppSettings Settings { get; init; }
    public bool WasCorrupt { get; init; }
    public string? BackupPath { get; init; }
}

/// <summary>
/// settings.json 載入/保存。損毀時：備份原檔 → 回傳預設值（不擲例外），符合規格 §18。
/// </summary>
public class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MousePilot", "settings.json");

    private readonly string _settingsPath;

    public SettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? DefaultSettingsPath;
    }

    public SettingsLoadResult Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new SettingsLoadResult { Settings = new AppSettings() };
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                           ?? throw new JsonException("settings 反序列化為 null");
            settings.Clamp();
            return new SettingsLoadResult { Settings = settings };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new SettingsLoadResult
            {
                Settings = new AppSettings(),
                WasCorrupt = true,
                BackupPath = TryBackupCorruptFile(),
            };
        }
    }

    public void Save(AppSettings settings)
    {
        settings.Clamp();
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    private string? TryBackupCorruptFile()
    {
        try
        {
            var backupPath = _settingsPath + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.bak";
            File.Copy(_settingsPath, backupPath, overwrite: true);
            return backupPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 備份失敗不影響「載入預設值」主流程；Phase 11 接上 LogService 後記錄
            return null;
        }
    }
}
