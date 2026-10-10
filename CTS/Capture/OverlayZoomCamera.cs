using System.Windows;

namespace CircleToSearch.Capture;

// Idea of zooming toward the pointer with inertia inspired by tsoding's boomer (https://github.com/tsoding/boomer).
// Maps the frozen screen (scene) onto the overlay (viewport) as viewport = scene * Scale + Offset.
// Zoom eases toward a target on a log scale, so every wheel notch feels the same at any magnification; panning
// keeps the velocity of a released drag and loses it to friction.
internal sealed class OverlayZoomCamera
{
    internal const double MaxScale = 8;
    private const double NotchFactor = 1.25;
    private const double ZoomRate = 9;
    private const double PanFriction = 5;
    private const double MaxFlingSpeed = 6000;
    private const double SettledLogScale = 1e-4;
    private const double SettledPanSpeed = 8;
    private const double BounceFrequency = 14;
    private const double BounceInKick = 1.5;
    private const double BounceOutKick = -1;
    private const double MaxBounceInVelocity = 3;
    private const double MaxBounceOutVelocity = -4.5;
    private static readonly double MaxBounceIn = Math.Log(1.08);
    private static readonly double MaxBounceOut = Math.Log(0.85);
    // Backing away waits until the zoom out has arrived: around the center, a screen still magnified toward a
    // corner would open a gap at the far edge.
    private static readonly double ArrivedUnzoomed = Math.Log(1.02);
    private const double CoverTolerance = 1e-3;

    private static readonly double MaxLogScale = Math.Log(MaxScale);

    private Rect _screen;
    private double _logScale;
    private double _logTarget;
    private double _scale = 1;
    private Vector _offset;
    private Point _pivot;
    private Vector _velocity;
    private double _bounce;
    private double _bounceVelocity;
    private Point _bouncePivot;

    // Includes a running bounce, so pointer mapping matches what is drawn.
    internal double Scale => _scale * Math.Exp(_bounce);

    internal Vector Offset => _bounce == 0
        ? _offset
        : (Vector)_bouncePivot - ((Vector)_bouncePivot - _offset) * Math.Exp(_bounce);

    internal bool IsZoomed => _logScale > SettledLogScale;

    // False while a bounce below the unzoomed screen leaves room around it, which then needs a backdrop.
    internal bool CoversScreen
    {
        get
        {
            var topLeft = ToViewport(_screen.TopLeft);
            var bottomRight = ToViewport(_screen.BottomRight);
            return topLeft.X <= _screen.Left + CoverTolerance && topLeft.Y <= _screen.Top + CoverTolerance &&
                   bottomRight.X >= _screen.Right - CoverTolerance &&
                   bottomRight.Y >= _screen.Bottom - CoverTolerance;
        }
    }

    internal bool IsMoving =>
        Math.Abs(_logTarget - _logScale) > SettledLogScale || _velocity.Length > SettledPanSpeed ||
        _bounce != 0 || _bounceVelocity != 0;

    // Where the frozen screen sits in the viewport; the overlay may reach a little past the monitor around it.
    internal void SetScreen(Rect screen)
    {
        _screen = screen;
        _offset = Clamp(_offset, _scale);
    }

    // True when the push went past a limit and bounced instead.
    internal bool ZoomBy(double notches, Point pivot)
    {
        if (!double.IsFinite(notches) || notches == 0) return false;
        if (notches > 0 && _logTarget >= MaxLogScale - SettledLogScale)
        {
            Kick(BounceInKick, pivot);
            return true;
        }
        if (notches < 0 && _logTarget <= SettledLogScale)
        {
            if (_logScale > ArrivedUnzoomed) return false;
            // Around the center the screen backs away evenly, rather than sliding toward the pointer.
            Kick(BounceOutKick, new Point(_screen.Left + _screen.Width / 2, _screen.Top + _screen.Height / 2));
            return true;
        }
        // Reversing direction drops what is left of the previous zoom, like a wheel that stops before turning back.
        if (Math.Sign(notches) != Math.Sign(_logTarget - _logScale)) _logTarget = _logScale;
        _logTarget = Math.Clamp(_logTarget + notches * Math.Log(NotchFactor), 0, MaxLogScale);
        _pivot = pivot;
        return false;
    }

    // False when there is nothing to undo or the view is already on its way back.
    internal bool Reset()
    {
        if (!IsZoomed || _logTarget == 0) return false;
        _velocity = default;
        _logTarget = 0;
        // Zooming out around the one point the view leaves in place lands exactly on the unzoomed screen.
        _pivot = new Point(_offset.X / (1 - _scale), _offset.Y / (1 - _scale));
        return true;
    }

    internal void PanBy(Vector delta)
    {
        _velocity = default;
        _offset = Clamp(_offset + delta, _scale);
    }

    internal void Fling(Vector velocity)
    {
        _velocity = velocity.Length > MaxFlingSpeed ? velocity * (MaxFlingSpeed / velocity.Length) : velocity;
    }

    // Glides the view by the distance, the way a smooth-scrolling wheel does.
    internal void ScrollBy(Vector distance) => _velocity += distance * PanFriction;

    internal void StopPan() => _velocity = default;

    // Returns how far the zoom moved on the log scale, positive when zooming in; a bounce does not count.
    internal double Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return 0;
        var zoomed = 0.0;
        var offset = _offset;
        if (Math.Abs(_logTarget - _logScale) > SettledLogScale)
        {
            var next = _logTarget + (_logScale - _logTarget) * Math.Exp(-ZoomRate * seconds);
            if (Math.Abs(_logTarget - next) <= SettledLogScale) next = _logTarget;
            var scale = Math.Exp(next);
            var scenePivot = ((Vector)_pivot - offset) / _scale;
            offset = (Vector)_pivot - scenePivot * scale;
            zoomed = next - _logScale;
            _logScale = next;
            _scale = scale;
        }

        if (_velocity.Length > SettledPanSpeed)
        {
            var decay = Math.Exp(-PanFriction * seconds);
            offset += _velocity * ((1 - decay) / PanFriction);
            _velocity *= decay;
        }
        else _velocity = default;

        var clamped = Clamp(offset, _scale);
        if (clamped.X != offset.X) _velocity.X = 0;
        if (clamped.Y != offset.Y) _velocity.Y = 0;
        _offset = clamped;
        AdvanceBounce(seconds);
        return zoomed;
    }

    internal Point ToViewport(Point scene) => new(scene.X * Scale + Offset.X, scene.Y * Scale + Offset.Y);

    internal Rect ToViewport(Rect scene) => scene.IsEmpty
        ? scene
        : new Rect(ToViewport(scene.TopLeft), ToViewport(scene.BottomRight));

    internal Point ToScene(Point viewport) => new((viewport.X - Offset.X) / Scale, (viewport.Y - Offset.Y) / Scale);

    // Positive past the maximum, negative below the unzoomed screen.
    private int BounceSign => Math.Sign(_bounce != 0 ? _bounce : _bounceVelocity);

    private void Kick(double velocity, Point pivot)
    {
        // A bounce of the other limit may still be settling; the new one replaces it rather than fighting it.
        if (BounceSign == -Math.Sign(velocity))
        {
            _bounce = 0;
            _bounceVelocity = 0;
        }
        // Rubber resistance: the further the screen is already stretched, the less a new push moves it.
        var room = Math.Clamp(1 - _bounce / (velocity > 0 ? MaxBounceIn : MaxBounceOut), 0, 1);
        _bounceVelocity = Math.Clamp(_bounceVelocity + velocity * room, MaxBounceOutVelocity, MaxBounceInVelocity);
        _bouncePivot = pivot;
    }

    // The exact critically damped spring stays stable across frame rates and returns without wobbling. Kicked
    // from rest it never crosses zero, so a bounce past the maximum only magnifies and keeps the screen covering
    // the viewport, and one below the unzoomed screen only shrinks it.
    private void AdvanceBounce(double seconds)
    {
        if (_bounce == 0 && _bounceVelocity == 0) return;
        var sign = BounceSign;
        var decay = Math.Exp(-BounceFrequency * seconds);
        var combined = _bounceVelocity + BounceFrequency * _bounce;
        var next = (_bounce + combined * seconds) * decay;
        _bounce = sign > 0 ? Math.Clamp(next, 0, MaxBounceIn) : Math.Clamp(next, MaxBounceOut, 0);
        _bounceVelocity = (_bounceVelocity - BounceFrequency * combined * seconds) * decay;
        if (_bounce != next && Math.Sign(_bounceVelocity) == sign) _bounceVelocity = 0;
        if (Math.Abs(_bounce) >= SettledLogScale || Math.Abs(_bounceVelocity) >= SettledLogScale) return;
        _bounce = 0;
        _bounceVelocity = 0;
    }

    // The frozen screen always covers the area it fills unzoomed, so no empty band opens along its edges.
    private Vector Clamp(Vector offset, double scale) => new(
        Math.Clamp(offset.X, _screen.Right * (1 - scale), _screen.Left * (1 - scale)),
        Math.Clamp(offset.Y, _screen.Bottom * (1 - scale), _screen.Top * (1 - scale)));
}
