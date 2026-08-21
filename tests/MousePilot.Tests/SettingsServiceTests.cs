using System.IO;
using MousePilot.Models;
using MousePilot.Services;

namespace MousePilot.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "MousePilotTests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void 檔案不存在時回傳預設值且不標記損毀()
    {
        var result = new SettingsService(SettingsPath).Load();
        Assert.False(result.WasCorrupt);
        Assert.Equal(120, result.Settings.IdleStartSeconds);
    }

    [Fact]
    public void Save會自動建立資料夾()
    {
        new SettingsService(SettingsPath).Save(new AppSettings());
        Assert.True(File.Exists(SettingsPath));
    }

    [Fact]
    public void Save後Load保留設定值()
    {
        var service = new SettingsService(SettingsPath);
        service.Save(new AppSettings
        {
            IdleStartSeconds = 300,
            MovementMode = MovementMode.Vertical,
            ReturnToOriginalPosition = false,
        });

        var loaded = new SettingsService(SettingsPath).Load().Settings;
        Assert.Equal(300, loaded.IdleStartSeconds);
        Assert.Equal(MovementMode.Vertical, loaded.MovementMode);
        Assert.False(loaded.ReturnToOriginalPosition);
    }

    [Fact]
    public void JSON使用camelCase欄位名稱()
    {
        new SettingsService(SettingsPath).Save(new AppSettings());
        var json = File.ReadAllText(SettingsPath);
        Assert.Contains("\"idleStartSeconds\"", json);
        Assert.Contains("\"movementMode\"", json);
        Assert.Contains("\"Random\"", json); // enum 以字串保存
    }

    [Fact]
    public void 損毀JSON不擲例外且載入預設值()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ 這不是合法 JSON !!!");

        var result = new SettingsService(SettingsPath).Load();

        Assert.True(result.WasCorrupt);
        Assert.Equal(120, result.Settings.IdleStartSeconds);
    }

    [Fact]
    public void 損毀JSON會備份原始檔案()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ broken");

        var result = new SettingsService(SettingsPath).Load();

        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        Assert.Equal("{ broken", File.ReadAllText(result.BackupPath!));
    }

    [Fact]
    public void 載入時超出範圍的值會被夾制()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{\"idleStartSeconds\": 1, \"movementPixels\": 9999}");

        var loaded = new SettingsService(SettingsPath).Load().Settings;

        Assert.Equal(5, loaded.IdleStartSeconds);
        Assert.Equal(100, loaded.MovementPixels);
    }

    [Fact]
    public void 收藏清單可保存與載回()
    {
        var service = new SettingsService(SettingsPath);
        service.Save(new AppSettings { FavoriteCursors = { "preset:Heart", "file:cat.png" } });

        var loaded = new SettingsService(SettingsPath).Load().Settings;

        Assert.Equal(new[] { "preset:Heart", "file:cat.png" }, loaded.FavoriteCursors);
        Assert.Contains("\"favoriteCursors\"", File.ReadAllText(SettingsPath));
    }
}
