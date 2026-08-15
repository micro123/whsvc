using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace WallhavenService;

public sealed partial class LogWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);
    private const int GwlHwndParent = -8;

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private readonly Window _owner;

    public LogWindow(Window owner)
    {
        InitializeComponent();
        _owner = owner;
        Title = "Wallhaven 壁纸服务 - 运行日志";
        SystemBackdrop = new MicaBackdrop();
        ConfigureWindow();
    }

    public void SetLogText(string text)
    {
        LogTextBox.Text = text;
        LogTextBox.Select(LogTextBox.Text.Length, 0);
    }

    private void ConfigureWindow()
    {
        var dpi = GetDpiForWindow(WindowNative.GetWindowHandle(this));
        var scale = dpi > 0 ? dpi / 96d : 1d;

        AppWindow.Resize(new SizeInt32(ToPhysicalPixels(900, scale), ToPhysicalPixels(600, scale)));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = ToPhysicalPixels(620, scale);
            presenter.PreferredMinimumHeight = ToPhysicalPixels(420, scale);
        }

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "App.ico"));
        SetOwnerWindow();
        CenterOnOwner();
    }


    private void SetOwnerWindow()
    {
        var logHandle = WindowNative.GetWindowHandle(this);
        var ownerHandle = WindowNative.GetWindowHandle(_owner);
        if (logHandle != IntPtr.Zero && ownerHandle != IntPtr.Zero)
            SetWindowLongPtr(logHandle, GwlHwndParent, ownerHandle);
    }

    private void CenterOnOwner()
    {
        var ownerWindow = _owner.AppWindow;
        var x = ownerWindow.Position.X + Math.Max(0, (ownerWindow.Size.Width - AppWindow.Size.Width) / 2);
        var y = ownerWindow.Position.Y + Math.Max(0, (ownerWindow.Size.Height - AppWindow.Size.Height) / 2);
        AppWindow.Move(new PointInt32(x, y));
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private static int ToPhysicalPixels(double value, double scale) =>
        (int)Math.Round(value * scale);
}
