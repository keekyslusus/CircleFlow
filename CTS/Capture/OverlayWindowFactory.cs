using CircleToSearch.Capture.OverlayInteractions;
using GdiBitmap = System.Drawing.Bitmap;
using GdiPoint = System.Drawing.Point;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Capture;

public interface IOverlayWindowFactory
{
    OverlayWindow Create(
        GdiBitmap frame,
        GdiRectangle monitor,
        GdiRectangle workArea,
        double scale,
        OverlayLaunchOptions options,
        Action<IOverlayCommand> publishCommand,
        GdiPoint entranceOrigin);
}

internal sealed class OverlayWindowFactory(IOverlayControllerFactory controllerFactory) : IOverlayWindowFactory
{
    private readonly IOverlayControllerFactory _controllerFactory = controllerFactory;

    public OverlayWindow Create(
        GdiBitmap frame,
        GdiRectangle monitor,
        GdiRectangle workArea,
        double scale,
        OverlayLaunchOptions options,
        Action<IOverlayCommand> publishCommand,
        GdiPoint entranceOrigin) =>
        new(
            frame,
            monitor,
            workArea,
            scale,
            options,
            publishCommand,
            _controllerFactory,
            entranceOrigin: entranceOrigin);
}
