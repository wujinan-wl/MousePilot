namespace MousePilot.Services;

public readonly record struct HotkeyCombo(uint Modifiers, uint VirtualKey);

/// <summary>快捷鍵字串（"Ctrl+Alt+F9"）與 RegisterHotKey 參數的互轉（純函式）。</summary>
public static class HotkeyParser
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    public static HotkeyCombo? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        uint modifiers = 0;
        uint? vk = null;
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim();
            switch (part)
            {
                case "Ctrl":
                    modifiers |= ModControl;
                    continue;
                case "Alt":
                    modifiers |= ModAlt;
                    continue;
                case "Shift":
                    modifiers |= ModShift;
                    continue;
                case "Win":
                    modifiers |= ModWin;
                    continue;
            }

            if (KeyToVk(part) is not { } parsed || vk is not null)
            {
                return null; // 不支援的主鍵，或已有第二個主鍵
            }

            vk = parsed;
        }

        if (modifiers == 0 || vk is null)
        {
            return null; // 必須至少一個修飾鍵 + 恰好一個主鍵
        }

        return new HotkeyCombo(modifiers, vk.Value);
    }

    public static string? Validate(string text)
        => Parse(text) is null
            ? "快捷鍵格式無效：需要至少一個修飾鍵（Ctrl/Alt/Shift/Win）加一個主鍵（F1~F24、A~Z、0~9），例如 Ctrl+Alt+F9。"
            : null;

    private static uint? KeyToVk(string part)
    {
        if (part.Length >= 2 && part[0] == 'F' && int.TryParse(part[1..], out var f) && f is >= 1 and <= 24)
        {
            return 0x70u + (uint)(f - 1); // VK_F1 = 0x70
        }

        if (part.Length == 1)
        {
            var c = part[0];
            if (c is >= 'A' and <= 'Z')
            {
                return (uint)c; // VK_A~VK_Z 與 ASCII 相同
            }

            if (c is >= '0' and <= '9')
            {
                return (uint)c; // VK_0~VK_9 與 ASCII 相同
            }
        }

        return null;
    }
}
