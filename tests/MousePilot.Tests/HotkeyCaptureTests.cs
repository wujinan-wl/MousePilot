using System.Windows.Input;
using MousePilot.Views;

namespace MousePilot.Tests;

public class HotkeyCaptureTests
{
    [Theory]
    [InlineData(Key.F9, ModifierKeys.Control | ModifierKeys.Alt, "Ctrl+Alt+F9")]
    [InlineData(Key.A, ModifierKeys.Control, "Ctrl+A")]
    [InlineData(Key.D5, ModifierKeys.Shift | ModifierKeys.Windows, "Shift+Win+5")]
    [InlineData(Key.F13, ModifierKeys.Alt, "Alt+F13")]
    public void 組合鍵轉為正規字串(Key key, ModifierKeys modifiers, string expected)
    {
        Assert.Equal(expected, HotkeyCapture.FromKeyEvent(key, modifiers));
    }

    [Theory]
    [InlineData(Key.F9, ModifierKeys.None)]              // 無修飾鍵
    [InlineData(Key.LeftCtrl, ModifierKeys.Control)]     // 修飾鍵本身
    [InlineData(Key.Escape, ModifierKeys.Control)]       // 不支援的主鍵
    public void 不完整或不支援的組合回傳null(Key key, ModifierKeys modifiers)
    {
        Assert.Null(HotkeyCapture.FromKeyEvent(key, modifiers));
    }
}
