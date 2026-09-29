using NAudio.Wave;

namespace CircleToSearch.MusicRecognition.Audio;

public readonly record struct AudioLevelFrame(TimeSpan Elapsed, double Rms, double Peak);

public readonly record struct MusicVisualizationFrame(
    TimeSpan Elapsed,
    double NormalizedLevel,
    double NormalizedPeak,
    bool IsTransient);

public interface IMusicVisualizationProgress
{
    // Called from the capture path. Implementations must return quickly and must not touch WPF directly.
    void Report(MusicVisualizationFrame frame);
}

public static class MusicVisualizationSettings
{
    public const double RippleSensitivity = 0.67;
}

public static class AudioLevelMeter
{
    public static AudioLevelFrame Measure(ReadOnlySpan<byte> bytes, WaveFormat format, TimeSpan elapsed)
    {
        var bytesPerSample = format.BitsPerSample / 8;
        if (bytesPerSample <= 0 || bytes.Length < bytesPerSample)
            return new AudioLevelFrame(elapsed, 0, 0);

        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                      format is WaveFormatExtensible extensible &&
                      extensible.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT;
        var isPcm = format.Encoding == WaveFormatEncoding.Pcm ||
                    format is WaveFormatExtensible pcmExtensible &&
                    pcmExtensible.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_PCM;
        if (!isFloat && !isPcm)
            throw new NotSupportedException($"Audio sample format '{format.Encoding}' is not supported.");

        double energy = 0;
        double peak = 0;
        var count = 0;
        for (var offset = 0; offset + bytesPerSample <= bytes.Length; offset += bytesPerSample)
        {
            double sample = isFloat && format.BitsPerSample == 32
                ? BitConverter.Int32BitsToSingle(System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]))
                : ReadPcm(bytes[offset..], format.BitsPerSample);
            if (!double.IsFinite(sample)) sample = 0;
            sample = Math.Clamp(sample, -1, 1);
            var absolute = Math.Abs(sample);
            energy += sample * sample;
            peak = Math.Max(peak, absolute);
            count++;
        }

        return count == 0
            ? new AudioLevelFrame(elapsed, 0, 0)
            : new AudioLevelFrame(elapsed, Math.Sqrt(energy / count), peak);
    }

    private static double ReadPcm(ReadOnlySpan<byte> bytes, int bitsPerSample) => bitsPerSample switch
    {
        16 => System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32768d,
        24 => ReadPcm24(bytes) / 8388608d,
        32 => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes) / 2147483648d,
        _ => throw new NotSupportedException($"PCM {bitsPerSample}-bit audio is not supported."),
    };

    private static int ReadPcm24(ReadOnlySpan<byte> bytes)
    {
        var value = bytes[0] | bytes[1] << 8 | bytes[2] << 16;
        return (value & 0x800000) == 0 ? value : value | unchecked((int)0xFF000000);
    }
}

public static class AudioLevelNormalizer
{
    private const double FloorDb = -60;
    private const double CeilingDb = -6;

    public static double Normalize(double amplitude)
    {
        if (!double.IsFinite(amplitude) || amplitude <= 0) return 0;
        var decibels = 20 * Math.Log10(Math.Min(amplitude, 1));
        return Math.Clamp((decibels - FloorDb) / (CeilingDb - FloorDb), 0, 1);
    }
}

public sealed record AudioTransientOptions(
    double Sensitivity = MusicVisualizationSettings.RippleSensitivity,
    double FastTimeConstantSeconds = 0.035,
    double SlowTimeConstantSeconds = 0.3,
    double MinimumLevel = 0.16,
    TimeSpan? RefractoryInterval = null)
{
    public TimeSpan EffectiveRefractoryInterval => RefractoryInterval ?? TimeSpan.FromMilliseconds(225);

    public double EffectiveOnsetThreshold =>
        0.018 + (1 - Math.Clamp(Sensitivity, 0, 1)) * 0.1;

    public double EffectiveRearmThreshold => EffectiveOnsetThreshold * 0.45;
}

public sealed class AudioTransientDetector
{
    private readonly AudioTransientOptions _options;
    private bool _initialized;
    private bool _armed = true;
    private double _fast;
    private double _slow;
    private TimeSpan _lastElapsed;
    private TimeSpan _lastTransient = TimeSpan.MinValue;

    public AudioTransientDetector(AudioTransientOptions? options = null)
    {
        _options = options ?? new AudioTransientOptions();
    }

    public MusicVisualizationFrame Process(AudioLevelFrame frame)
    {
        var level = AudioLevelNormalizer.Normalize(frame.Rms);
        var peak = AudioLevelNormalizer.Normalize(frame.Peak);
        if (!_initialized)
        {
            _initialized = true;
            _fast = _slow = level;
            _lastElapsed = frame.Elapsed;
            return new MusicVisualizationFrame(frame.Elapsed, level, peak, false);
        }

        var seconds = Math.Max(0.001, (frame.Elapsed - _lastElapsed).TotalSeconds);
        _lastElapsed = frame.Elapsed;
        _fast += Alpha(seconds, _options.FastTimeConstantSeconds) * (level - _fast);
        _slow += Alpha(seconds, _options.SlowTimeConstantSeconds) * (level - _slow);
        var onset = Math.Max(0, _fast - _slow);

        if (!_armed && onset <= _options.EffectiveRearmThreshold)
            _armed = true;

        var outsideRefractory = _lastTransient == TimeSpan.MinValue ||
                                frame.Elapsed - _lastTransient >= _options.EffectiveRefractoryInterval;
        var transient = _armed && outsideRefractory && level >= _options.MinimumLevel &&
                        onset >= _options.EffectiveOnsetThreshold;
        if (transient)
        {
            _armed = false;
            _lastTransient = frame.Elapsed;
        }

        return new MusicVisualizationFrame(frame.Elapsed, level, peak, transient);
    }

    public void Reset()
    {
        _initialized = false;
        _armed = true;
        _fast = _slow = 0;
        _lastElapsed = default;
        _lastTransient = TimeSpan.MinValue;
    }

    private static double Alpha(double elapsed, double timeConstant) =>
        1 - Math.Exp(-elapsed / Math.Max(0.001, timeConstant));
}
