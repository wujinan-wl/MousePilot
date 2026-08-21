using System.Windows.Input;

namespace MousePilot.Views;

/// <summary>把 WPF 按鍵事件轉為 "Ctrl+Alt+F9" 正規字串（純函式；順序固定 Ctrl→Alt→Shift→Win）。</summary>
public static class HotkeyCapture
{
    public static string? FromKeyEvent(Key key, ModifierKeys modifiers)
    {
        if (modifiers == ModifierKeys.None)
        {
            return null;
        }

        if (KeyName(key) is not { } name)
        {
            return null; // 修飾鍵本身或不支援的鍵
        }

        var parts = new List<string>(5);
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(name);
        return string.Join("+", parts);
    }

    private static string? KeyName(Key key) => key switch
    {
        >= Key.F1 and <= Key.F24 => $"F{key - Key.F1 + 1}",
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        _ => null,
    };
}
