using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CircleToSearch.MusicRecognition.Audio;

public static class AudioPreprocessor
{
    public const int TargetSampleRate = 16_000;

    public static float[] FromCapture(CapturedAudio capture, int maximumSeconds)
    {
        using var stream = new MemoryStream(capture.Data, writable: false);
        using var raw = new RawSourceWaveStream(stream, capture.Format);
        return ConvertAndSelect(raw.ToSampleProvider(), maximumSeconds);
    }

    public static double CalculateRms(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return 0;
        double sum = 0;
        foreach (var sample in samples) sum += sample * sample;
        return Math.Sqrt(sum / samples.Length);
    }

    private static float[] ConvertAndSelect(ISampleProvider source, int maximumSeconds)
    {
        ISampleProvider mono = source.WaveFormat.Channels == 1 ? source : new MonoSampleProvider(source);
        ISampleProvider resampled = mono.WaveFormat.SampleRate == TargetSampleRate
            ? mono
            : new WdlResamplingSampleProvider(mono, TargetSampleRate);

        var samples = new List<float>();
        var readBuffer = new float[TargetSampleRate];
        int read;
        while ((read = resampled.Read(readBuffer.AsSpan())) > 0)
            samples.AddRange(readBuffer.AsSpan(0, read).ToArray());

        var maximum = maximumSeconds * TargetSampleRate;
        if (samples.Count <= maximum) return samples.ToArray();
        var start = samples.Count - maximum;
        return samples.GetRange(start, maximum).ToArray();
    }
}
