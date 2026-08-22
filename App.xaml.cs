using Microsoft.UI.Xaml;
using WallhavenService.Services;

namespace WallhavenService;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private bool _shutdownStarted;
    private bool _activationPending;

    public WallpaperOrchestrator Orchestrator { get; } = new();

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mainWindow = new MainWindow(Orchestrator, ShutdownAsync);
        _mainWindow.Activate();

        if (_activationPending)
        {
            _activationPending = false;
            _mainWindow.ShowFromExternalActivation();
        }
    }

    internal void ActivateMainWindow()
    {
        if (_shutdownStarted)
            return;

        if (_mainWindow is null)
        {
            _activationPending = true;
            return;
        }

        _mainWindow.ShowFromExternalActivation();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        _mainWindow?.ReportUnhandledException(e.Exception);
    }

    private async Task ShutdownAsync()
    {
        if (_shutdownStarted)
            return;

        _shutdownStarted = true;
        await Orchestrator.DisposeAsync();
        Exit();
    }
}
