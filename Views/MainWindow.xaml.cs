using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MousePilot.ViewModels;

namespace MousePilot.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // TextBox 預設 LostFocus 才寫回 binding；直接關窗不會失焦，需先手動 commit（final review Issue 2）
        (Keyboard.FocusedElement as TextBox)?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        (DataContext as MainViewModel)?.SaveSettings();
        base.OnClosing(e);
    }

    private void OnToggleHotkeyKeyDown(object sender, KeyEventArgs e) => CaptureHotkey(e, isToggle: true);

    private void OnRestoreHotkeyKeyDown(object sender, KeyEventArgs e) => CaptureHotkey(e, isToggle: false);

    private void CaptureHotkey(KeyEventArgs e, bool isToggle)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key; // Alt 組合鍵的實際主鍵在 SystemKey
        if (HotkeyCapture.FromKeyEvent(key, Keyboard.Modifiers) is not { } gesture)
        {
            return; // 僅修飾鍵或不支援的鍵：等待完整組合
        }

        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (isToggle)
        {
            vm.ToggleHotkeyText = gesture;
        }
        else
        {
            vm.RestoreCursorHotkeyText = gesture;
        }
    }
}
