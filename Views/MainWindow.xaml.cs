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
        (Keyboard.FocusedElement as System.Windows.Controls.TextBox)?.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        (DataContext as MainViewModel)?.SaveSettings();
        base.OnClosing(e);
    }
}
