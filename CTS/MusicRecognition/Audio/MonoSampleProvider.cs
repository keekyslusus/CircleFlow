using NAudio.Wave;

namespace CircleToSearch.MusicRecognition.Audio;

internal sealed class MonoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _sourceChannels;
    private float[] _sourceBuffer = [];

    public MonoSampleProvider(ISampleProvider source)
    {
        _source = source;
        _sourceChannels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public int Read(Span<float> buffer)
    {
        var required = buffer.Length * _sourceChannels;
        if (_sourceBuffer.Length < required) _sourceBuffer = new float[required];
        var read = _source.Read(_sourceBuffer.AsSpan(0, required));
        var frames = read / _sourceChannels;

        for (var frame = 0; frame < frames; frame++)
        {
            var sum = 0f;
            for (var channel = 0; channel < _sourceChannels; channel++)
                sum += _sourceBuffer[frame * _sourceChannels + channel];
            buffer[frame] = sum / _sourceChannels;
        }

        return frames;
    }
}
