namespace CircleToSearch.Capture.OverlayInteractions;

internal interface IOverlayControllerFactory
{
    OverlayControllers Create(OverlayControllerContext context);
}

internal sealed class OverlayControllerFactory(
    Func<OverlayControllerContext, OverlayControllers> createControllers) : IOverlayControllerFactory
{
    private readonly Func<OverlayControllerContext, OverlayControllers> _createControllers = createControllers;

    public OverlayControllers Create(OverlayControllerContext context)
    {
        return _createControllers(context)
            ?? throw new InvalidOperationException("The overlay controller composition returned no controllers.");
    }
}
