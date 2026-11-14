using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System.Runtime.InteropServices;
using WallhavenService.Services;

namespace WallhavenService;

public static class Program
{
    private const string InstanceKey = "WallhavenService.MainInstance";
    private static DispatcherQueue? _dispatcherQueue;
    private static App? _app;
    private static int _activationPending;

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoWaitForMultipleObjects(
        uint flags, uint timeout, uint handleCount, IntPtr[] handles, out uint index);

    [STAThread]
    public static void Main(string[] args)
    {
        // An async Main gets a generated entry point without STAThread. WinUI can
        // render on that MTA thread, but UI Automation then fails with WRONG_THREAD.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
                AppDiagnostics.WriteException("Unhandled process exception", exception);
        };
        AppDiagnostics.Write("Process entry");
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("WinUI 必须从同步的 STA 主线程启动。");

        WinRT.ComWrappersSupport.InitializeComWrappers();

        var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        var mainInstance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!mainInstance.IsCurrent)
        {
            RedirectActivation(mainInstance, activationArgs);
            return;
        }

        mainInstance.Activated += OnActivated;
        Application.Start(_ =>
        {
            AppDiagnostics.Write("Starting XAML application");
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(_dispatcherQueue));
            _app = new App();

            if (Interlocked.Exchange(ref _activationPending, 0) != 0)
                _app.ActivateMainWindow();
        });
    }

    private static void RedirectActivation(AppInstance mainInstance, AppActivationArguments args)
    {
        AppDiagnostics.Write("Redirecting activation to existing instance");
        using var completed = new EventWaitHandle(false, EventResetMode.ManualReset);
        var redirect = Task.Run(async () =>
        {
            try
            {
                await mainInstance.RedirectActivationToAsync(args);
            }
            finally
            {
                completed.Set();
            }
        });

        // Pump COM while waiting on the STA thread, as recommended by AppLifecycle.
        // A plain Task.Wait/GetResult here can deadlock activation redirection.
        Marshal.ThrowExceptionForHR(CoWaitForMultipleObjects(
            0, uint.MaxValue, 1, [completed.SafeWaitHandle.DangerousGetHandle()], out _));
        redirect.GetAwaiter().GetResult();
        AppDiagnostics.Write("Activation redirection completed");
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
