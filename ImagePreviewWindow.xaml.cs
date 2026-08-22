using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage.Streams;
using WallhavenService.Models;
using WinRT.Interop;

namespace WallhavenService;

public sealed partial class ImagePreviewWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);
    private const int GwlHwndParent = -8;

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private const float MinimumZoom = 0.25f;
    private const float MaximumZoom = 4f;
    private const float ZoomStep = 1.15f;

    private readonly WallpaperItem _wallpaper;
    private readonly Window _owner;
    private readonly string? _localImagePath;
    private readonly HttpClient _httpClient = new();
    private byte[]? _imageBytes;
    private InMemoryRandomAccessStream? _clipboardImageStream;
    private bool _isPanning;
    private uint _panPointerId;
    private Point _panStartPoint;
    private double _panStartHorizontalOffset;
    private double _panStartVerticalOffset;
    private bool _centerInitialViewPending;
    private bool _initialViewCentered;

    public ImagePreviewWindow(WallpaperItem wallpaper, Window owner, string? localImagePath)
    {
        InitializeComponent();
        _wallpaper = wallpaper;
        _owner = owner;
        _localImagePath = localImagePath;

        Title = $"Wallhaven - {wallpaper.Id}";
        TitleText.Text = $"{wallpaper.SearchTag} · {wallpaper.Resolution}";
        SubtitleText.Text = $"ID: {wallpaper.Id}    Purity: {wallpaper.Purity.ToUpperInvariant()}";
        UpdateZoomText(1f);

        ConfigureWindow();
        ImageScrollViewer.LayoutUpdated += ImageScrollViewer_OnLayoutUpdated;
        Closed += PreviewWindow_Closed;
        _ = LoadImageAsync();
    }

    private void ConfigureWindow()
    {
        SystemBackdrop = new MicaBackdrop();
        var dpi = GetDpiForWindow(WindowNative.GetWindowHandle(this));
        var scale = dpi > 0 ? dpi / 96d : 1d;

        AppWindow.Resize(new SizeInt32(ToPhysicalPixels(1100, scale), ToPhysicalPixels(760, scale)));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = ToPhysicalPixels(760, scale);
            presenter.PreferredMinimumHeight = ToPhysicalPixels(540, scale);
        }

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "App.ico"));
        SetOwnerWindow();
        CenterOnOwner();
    }


    private void SetOwnerWindow()
    {
        var previewHandle = WindowNative.GetWindowHandle(this);
        var ownerHandle = WindowNative.GetWindowHandle(_owner);
        if (previewHandle != IntPtr.Zero && ownerHandle != IntPtr.Zero)
            SetWindowLongPtr(previewHandle, GwlHwndParent, ownerHandle);
    }

    private void CenterOnOwner()
    {
        var ownerWindow = _owner.AppWindow;
        var x = ownerWindow.Position.X + Math.Max(0, (ownerWindow.Size.Width - AppWindow.Size.Width) / 2);
        var y = ownerWindow.Position.Y + Math.Max(0, (ownerWindow.Size.Height - AppWindow.Size.Height) / 2);
        AppWindow.Move(new PointInt32(x, y));
    }

    private async Task LoadImageAsync()
    {
        try
        {
            var imageBytes = !string.IsNullOrWhiteSpace(_localImagePath) && File.Exists(_localImagePath)
                ? await File.ReadAllBytesAsync(_localImagePath)
                : await _httpClient.GetByteArrayAsync(_wallpaper.ImageUrl);
            _imageBytes = imageBytes;

            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(imageBytes);
                await writer.StoreAsync();
                writer.DetachStream();
            }

            stream.Seek(0);
            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            FullImage.Source = image;
            _centerInitialViewPending = true;
            LoadingText.Visibility = Visibility.Collapsed;
            CopyImageMenuItem.IsEnabled = true;
        }
        catch (Exception ex)
        {
            LoadingText.Text = $"原图加载失败：{ex.Message}";
        }
    }


    private void ImageScrollViewer_OnLayoutUpdated(object? sender, object e)
    {
        if (!_centerInitialViewPending || _initialViewCentered)
            return;

        if (ImageScrollViewer.ViewportWidth <= 0 || ImageScrollViewer.ViewportHeight <= 0 ||
            ImageScrollViewer.ExtentWidth <= 0 || ImageScrollViewer.ExtentHeight <= 0)
        {
            return;
        }

        var horizontalOffset = Math.Max(0, (ImageScrollViewer.ExtentWidth - ImageScrollViewer.ViewportWidth) / 2);
        var verticalOffset = Math.Max(0, (ImageScrollViewer.ExtentHeight - ImageScrollViewer.ViewportHeight) / 2);
        ImageScrollViewer.ChangeView(horizontalOffset, verticalOffset, null, true);
        _initialViewCentered = true;
        _centerInitialViewPending = false;
        ImageScrollViewer.LayoutUpdated -= ImageScrollViewer_OnLayoutUpdated;
    }

    private void ImageScrollViewer_OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(ImageScrollViewer);
        var currentZoom = ImageScrollViewer.ZoomFactor;
        var nextZoom = point.Properties.MouseWheelDelta > 0
            ? currentZoom * ZoomStep
            : currentZoom / ZoomStep;
        nextZoom = Math.Clamp(nextZoom, MinimumZoom, MaximumZoom);

        SetZoomAtPoint(nextZoom, point.Position);
        e.Handled = true;
    }

    private void ImageScrollViewer_OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(ImageScrollViewer);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        _isPanning = true;
        _panPointerId = e.Pointer.PointerId;
        _panStartPoint = point.Position;
        _panStartHorizontalOffset = ImageScrollViewer.HorizontalOffset;
        _panStartVerticalOffset = ImageScrollViewer.VerticalOffset;
        ImageScrollViewer.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void ImageScrollViewer_OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPanning || e.Pointer.PointerId != _panPointerId)
            return;

        var point = e.GetCurrentPoint(ImageScrollViewer);
        var deltaX = point.Position.X - _panStartPoint.X;
        var deltaY = point.Position.Y - _panStartPoint.Y;
        var horizontalOffset = Math.Max(0, _panStartHorizontalOffset - deltaX);
        var verticalOffset = Math.Max(0, _panStartVerticalOffset - deltaY);
        ImageScrollViewer.ChangeView(horizontalOffset, verticalOffset, null, true);
        e.Handled = true;
    }

    private void ImageScrollViewer_OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerId == _panPointerId)
            EndPanning(e.Pointer);
    }

    private void ImageScrollViewer_OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerId == _panPointerId)
            EndPanning(e.Pointer);
    }

    private void ImageScrollViewer_OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerId == _panPointerId)
            EndPanning(null);
    }

    private void ImageScrollViewer_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var currentZoom = ImageScrollViewer.ZoomFactor;
        var nextZoom = currentZoom > 1.01f ? 1f : 2f;
        if (nextZoom <= 1f)
        {
            ImageScrollViewer.ChangeView(0, 0, nextZoom, true);
        }
        else
        {
            SetZoomAtPoint(nextZoom, e.GetPosition(ImageScrollViewer));
        }

        e.Handled = true;
    }

    private void SetZoomAtPoint(float zoomFactor, Point position)
    {
        var currentZoom = ImageScrollViewer.ZoomFactor;
        if (Math.Abs(currentZoom - zoomFactor) < 0.001f)
            return;

        var contentX = (ImageScrollViewer.HorizontalOffset + position.X) / currentZoom;
        var contentY = (ImageScrollViewer.VerticalOffset + position.Y) / currentZoom;
        var horizontalOffset = Math.Max(0, contentX * zoomFactor - position.X);
        var verticalOffset = Math.Max(0, contentY * zoomFactor - position.Y);
        ImageScrollViewer.ChangeView(horizontalOffset, verticalOffset, zoomFactor, true);
        UpdateZoomText(zoomFactor);
    }

    private void EndPanning(Pointer? pointer)
    {
        if (pointer is not null)
            ImageScrollViewer.ReleasePointerCapture(pointer);

        _isPanning = false;
        _panPointerId = 0;
    }

    private void UpdateZoomText(float zoomFactor) => ZoomText.Text = $"{zoomFactor * 100:0}%";

    private async void CopyImageMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (_imageBytes is null)
            return;

        CopyImageMenuItem.IsEnabled = false;
        InMemoryRandomAccessStream? clipboardStream = null;

        try
        {
            clipboardStream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(clipboardStream))
            {
                writer.WriteBytes(_imageBytes);
                await writer.StoreAsync();
                writer.DetachStream();
            }

            clipboardStream.Seek(0);
            var package = new DataPackage();
            package.SetBitmap(RandomAccessStreamReference.CreateFromStream(clipboardStream));
            Clipboard.SetContent(package);
            Clipboard.Flush();

            _clipboardImageStream?.Dispose();
            _clipboardImageStream = clipboardStream;
            clipboardStream = null;
            CopyStatusText.Text = "图片已复制";
        }
        catch (Exception ex)
        {
            CopyStatusText.Text = $"复制失败：{ex.Message}";
        }
        finally
        {
            clipboardStream?.Dispose();
            CopyStatusText.Visibility = Visibility.Visible;
            CopyImageMenuItem.IsEnabled = _imageBytes is not null;
        }
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private void PreviewWindow_Closed(object sender, WindowEventArgs args)
    {
        _clipboardImageStream?.Dispose();
        _httpClient.Dispose();
    }

    private static int ToPhysicalPixels(double value, double scale) =>
        (int)Math.Round(value * scale);
}
