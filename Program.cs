using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace WallhavenService;

public static class Program
{
    private const string InstanceKey = "WallhavenService.MainInstance";
    private static DispatcherQueue? _dispatcherQueue;
    private static App? _app;
    private static int _activationPending;

    [STAThread]
    public static async Task Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        var mainInstance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!mainInstance.IsCurrent)
        {
            await mainInstance.RedirectActivationToAsync(activationArgs);
            return;
        }

        mainInstance.Activated += OnActivated;
        Application.Start(_ =>
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(_dispatcherQueue));
            _app = new App();

            if (Interlocked.Exchange(ref _activationPending, 0) != 0)
                _app.ActivateMainWindow();
        });
    }

    private static void OnActivated(object? sender, AppActivationArguments args)
    {
        var dispatcherQueue = _dispatcherQueue;
        if (dispatcherQueue is not null &&
            dispatcherQueue.TryEnqueue(() => _app?.ActivateMainWindow()))
        {
            return;
        }

        Interlocked.Exchange(ref _activationPending, 1);
    }
}
