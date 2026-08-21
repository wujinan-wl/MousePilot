using MousePilot.Services;

namespace MousePilot.Tests;

public class HotkeyParserTests
{
    [Theory]
    [InlineData("Ctrl+Alt+F9", HotkeyParser.ModControl | HotkeyParser.ModAlt, 0x78u)]
    [InlineData("Ctrl+Alt+F10", HotkeyParser.ModControl | HotkeyParser.ModAlt, 0x79u)]
    [InlineData("Shift+A", HotkeyParser.ModShift, 0x41u)]
    [InlineData("Win+0", HotkeyParser.ModWin, 0x30u)]
    [InlineData("Ctrl+Alt+Shift+Z", HotkeyParser.ModControl | HotkeyParser.ModAlt | HotkeyParser.ModShift, 0x5Au)]
    [InlineData("Ctrl+F24", HotkeyParser.ModControl, 0x87u)]
    public void 合法組合解析(string text, uint modifiers, uint vk)
    {
        var combo = HotkeyParser.Parse(text);
        Assert.NotNull(combo);
        Assert.Equal(modifiers, combo!.Value.Modifiers);
        Assert.Equal(vk, combo.Value.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("F9")]          // 無修飾鍵
    [InlineData("Ctrl+")]       // 無主鍵
    [InlineData("Ctrl+Alt")]    // 僅修飾鍵
    [InlineData("Ctrl+Esc")]    // 不支援的主鍵
    [InlineData("Ctrl+F25")]    // 超出 F24
    [InlineData("Ctrl+A+B")]    // 兩個主鍵
    public void 無效組合回傳null(string text)
    {
        Assert.Null(HotkeyParser.Parse(text));
    }

    [Fact]
    public void Validate對無效組合回傳錯誤訊息()
    {
        Assert.NotNull(HotkeyParser.Validate("F9"));
        Assert.Contains("修飾鍵", HotkeyParser.Validate("F9"));
    }

    [Fact]
    public void Validate對合法組合回傳null()
    {
        Assert.Null(HotkeyParser.Validate("Ctrl+Alt+F9"));
    }
}
