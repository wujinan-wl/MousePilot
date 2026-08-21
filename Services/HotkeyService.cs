using System.Windows.Interop;
using MousePilot.Native;

namespace MousePilot.Services;

/// <summary>
/// 全域快捷鍵（規格 §17）。真實路徑掛在隱藏 message-only 視窗（HWND_MESSAGE），
/// 不依賴可能藏在系統匣的 MainWindow；註冊函式可注入供測試（xUnit MTA 無法建 HwndSource）。
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly Func<int, uint, uint, bool> _registerFn;
    private readonly Func<int, bool> _unregisterFn;
    private readonly Dictionary<int, HotkeyCombo> _registered = new();
    private HwndSource? _source;

    public event Action<int>? HotkeyPressed;

    public HotkeyService(Func<int, uint, uint, bool>? registerFn = null, Func<int, bool>? unregisterFn = null)
    {
        _registerFn = registerFn ?? ((id, mod, vk) => NativeMethods.RegisterHotKey(EnsureSource(), id, mod, vk));
        _unregisterFn = unregisterFn ?? (id => NativeMethods.UnregisterHotKey(EnsureSource(), id));
    }

    /// <summary>註冊（同 id 重註冊會先解除舊組合）。false = 被其他程式占用或失敗，且不留殘留註冊。</summary>
    public bool Register(int id, HotkeyCombo combo)
    {
        Unregister(id);
        if (!_registerFn(id, combo.Modifiers, combo.VirtualKey))
        {
            return false;
        }

        _registered[id] = combo;
        return true;
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id))
        {
            _unregisterFn(id);
        }
    }

    /// <summary>測試鉤：直接觸發 HotkeyPressed（WndProc 路徑無法在無訊息迴圈的測試中驅動）。</summary>
    public void SimulatePress(int id) => HotkeyPressed?.Invoke(id);

    private IntPtr EnsureSource()
    {
        if (_source is null)
        {
            var parameters = new HwndSourceParameters("MousePilotHotkeyWindow")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0,
                ParentWindow = HwndMessage,
            };
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
        }

        return _source.Handle;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            HotkeyPressed?.Invoke(wParam.ToInt32());
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _registered.Keys.ToList())
        {
            Unregister(id);
        }

        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
    }
}
