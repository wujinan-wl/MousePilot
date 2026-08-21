using System.Windows;
using MousePilot.Services;
using MousePilot.ViewModels;
using MousePilot.Views;

namespace MousePilot;

public partial class App : System.Windows.Application
{
    private MainViewModel? _mainViewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mainViewModel = new MainViewModel(new SettingsService());
        var window = new MainWindow { DataContext = _mainViewModel };
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mainViewModel?.SaveSettings();
        _mainViewModel?.Dispose();
        base.OnExit(e);
    }
}
