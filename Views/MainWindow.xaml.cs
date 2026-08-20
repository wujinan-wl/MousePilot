using System.ComponentModel;
using System.Windows;
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
        (DataContext as MainViewModel)?.SaveSettings();
        base.OnClosing(e);
    }
}
