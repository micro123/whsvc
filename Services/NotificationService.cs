using Forms = System.Windows.Forms;

namespace WallhavenService.Services;

public sealed class NotificationService
{
    private readonly Forms.NotifyIcon _notifyIcon;

    public NotificationService(Forms.NotifyIcon notifyIcon) => _notifyIcon = notifyIcon;

    public void Show(string title, string message, Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info)
    {
        _notifyIcon.ShowBalloonTip(2500, title, message, icon);
    }
}
