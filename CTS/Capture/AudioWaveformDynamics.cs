namespace CircleToSearch.Capture;

internal sealed class AudioWaveformDynamics
{
    private const double AttackTimeSeconds = 0.055;
    private const double ReleaseTimeSeconds = 0.17;
    private const double MaximumStepSeconds = 0.1;

    internal double TargetLevel { get; private set; }

    internal double Level { get; private set; }

    internal void Report(double normalizedLevel)
    {
        TargetLevel = double.IsFinite(normalizedLevel)
            ? Math.Clamp(normalizedLevel, 0, 1)
            : 0;
    }

    internal void Advance(double elapsedSeconds)
    {
        var step = double.IsFinite(elapsedSeconds)
            ? Math.Clamp(elapsedSeconds, 0, MaximumStepSeconds)
            : 0;
        if (step == 0) return;

        var timeConstant = TargetLevel > Level
            ? AttackTimeSeconds
            : ReleaseTimeSeconds;
        var alpha = 1 - Math.Exp(-step / timeConstant);
        Level += (TargetLevel - Level) * alpha;
    }

    internal void Reset()
    {
        TargetLevel = 0;
        Level = 0;
    }
}
