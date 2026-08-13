using System.Windows;
using WallhavenService.Services;

namespace WallhavenService;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;

    public WallpaperOrchestrator Orchestrator { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mainWindow = new MainWindow(Orchestrator);
        MainWindow = _mainWindow;
        _mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await Orchestrator.DisposeAsync();
        base.OnExit(e);
    }
}
