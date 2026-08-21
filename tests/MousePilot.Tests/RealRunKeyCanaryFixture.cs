using Microsoft.Win32;

namespace MousePilot.Tests;

/// <summary>
/// 結構性防護：suite 開始時快照真實 Run key 的 MousePilot 值，結束時驗證未被改動。
/// 任何測試若污染真實 Run key（例如忘記注入 StartupService），Dispose 會擲出讓 suite 失敗。
/// </summary>
public sealed class RealRunKeyCanaryFixture : IDisposable
{
    private const string RealRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly object? _before;

    public RealRunKeyCanaryFixture()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RealRunKey);
        _before = key?.GetValue("MousePilot");
    }

    public void Dispose()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RealRunKey);
        var after = key?.GetValue("MousePilot");
        if (!Equals(_before, after))
        {
            throw new InvalidOperationException(
                $"測試污染了真實 Run key！before='{_before}', after='{after}'——檢查是否有 MainViewModel/StartupService 建構未注入測試路徑。");
        }
    }
}

[CollectionDefinition("RealRunKeyCanary")]
public class RealRunKeyCanaryCollection : ICollectionFixture<RealRunKeyCanaryFixture>
{
}
