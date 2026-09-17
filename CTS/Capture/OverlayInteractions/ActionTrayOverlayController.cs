using System.Windows;

namespace CircleToSearch.Capture.OverlayInteractions;

internal sealed class ActionTrayOverlayController : IDisposable
{
    private readonly ActionTrayVisual _visual;
    private readonly FrameworkElement _rippleRoot;
    private readonly List<IDisposable> _controlRipples = [];
    private bool _dismissed;
    private bool _ripplesAttached;
    private bool _disposed;

    internal ActionTrayOverlayController(ActionTrayVisual visual, FrameworkElement rippleRoot)
    {
        _visual = visual;
        _rippleRoot = rippleRoot;
    }

    internal int ControlRippleCount => _controlRipples.Count;

    internal void ShowEntrance()
    {
        if (_disposed) return;
        if (!_dismissed) ActionTrayTransitions.BeginEntrance(_visual);
        if (_ripplesAttached) return;
        _ripplesAttached = true;
        _controlRipples.AddRange(OverlayVisualResources.AttachControlRipples(_rippleRoot));
    }

    internal void HideForSelection()
    {
        if (_disposed) return;
        _dismissed = true;
        ActionTrayTransitions.BeginExit(_visual);
    }

    internal void Restore()
    {
        if (_disposed) return;
        _dismissed = false;
        ActionTrayTransitions.BeginReturn(_visual);
    }

    internal void BeginExit()
    {
        if (_disposed) return;
        _dismissed = true;
        ActionTrayTransitions.BeginExit(_visual);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var ripple in _controlRipples) ripple.Dispose();
        _controlRipples.Clear();
    }
}
