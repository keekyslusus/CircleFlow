// SPDX-License-Identifier: GPL-3.0-or-later

using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace CircleToSearch.MusicRecognition.Fingerprinting;

public sealed class ShazamSignatureGenerator
{
    private const int FftSize = 2048;
    private const int FftResultSize = FftSize / 2 + 1;
    private const int HopSize = 128;
    private const int HistorySize = 256;
    private static readonly float[] HanningWindow = CreateHanningWindow();
    private static readonly int[] SpreadOffsets = [1, 3, 6];
    private static readonly int[] FrequencyOffsets = [-10, -7, -4, -3, 1, 2, 5, 8];
    private static readonly int[] TimeOffsets = [-53, -45, 165, 172, 179, 186, 193, 200, 214, 221, 228, 235, 242, 249];

    private readonly short[] _sampleRing = new short[FftSize];
    private readonly float[][] _fftOutputs = CreateHistory();
    private readonly float[][] _spreadFftOutputs = CreateHistory();
    private readonly Complex32[] _fftBuffer = new Complex32[FftSize];
    private int _sampleRingIndex;
    private int _fftOutputIndex;
    private int _spreadFftOutputIndex;
    private uint _spreadFftsDone;
    private ShazamSignature _signature = null!;

    public ShazamSignature Generate(
        ReadOnlySpan<float> mono16KhzSamples,
        CancellationToken cancellationToken = default)
    {
        Reset(mono16KhzSamples.Length);
        var completeChunkCount = mono16KhzSamples.Length / HopSize;
        Span<short> chunk = stackalloc short[HopSize];
        for (var chunkIndex = 0; chunkIndex < completeChunkCount; chunkIndex++)
        {
            if ((chunkIndex & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
            var source = mono16KhzSamples.Slice(chunkIndex * HopSize, HopSize);
            for (var index = 0; index < HopSize; index++)
            {
                var scaled = source[index] * 32768f;
                chunk[index] = (short)Math.Clamp(scaled, short.MinValue, short.MaxValue);
            }
            DoFft(chunk);
            DoPeakSpreading();
            _spreadFftsDone++;
            if (_spreadFftsDone >= 46) DoPeakRecognition();
        }
        return _signature;
    }

    private void Reset(int sampleCount)
    {
        Array.Clear(_sampleRing);
        foreach (var output in _fftOutputs) Array.Clear(output);
        foreach (var output in _spreadFftOutputs) Array.Clear(output);
        _sampleRingIndex = 0;
        _fftOutputIndex = 0;
        _spreadFftOutputIndex = 0;
        _spreadFftsDone = 0;
        _signature = new ShazamSignature(sampleCount);
    }

    private void DoFft(ReadOnlySpan<short> chunk)
    {
        chunk.CopyTo(_sampleRing.AsSpan(_sampleRingIndex, HopSize));
        _sampleRingIndex = (_sampleRingIndex + HopSize) & (FftSize - 1);
        for (var index = 0; index < FftSize; index++)
        {
            var sample = _sampleRing[(index + _sampleRingIndex) & (FftSize - 1)];
            _fftBuffer[index] = new Complex32(sample * HanningWindow[index], 0f);
        }
        Fourier.Forward(_fftBuffer, FourierOptions.Matlab);
        var output = _fftOutputs[_fftOutputIndex];
        for (var index = 0; index < FftResultSize; index++)
        {
            var value = _fftBuffer[index];
            output[index] = MathF.Max(
                (value.Real * value.Real + value.Imaginary * value.Imaginary) / (1 << 17),
                0.0000000001f);
        }
        _fftOutputIndex = (_fftOutputIndex + 1) & (HistorySize - 1);
    }

    private void DoPeakSpreading()
    {
        var fft = _fftOutputs[(_fftOutputIndex - 1) & (HistorySize - 1)];
        var spread = _spreadFftOutputs[_spreadFftOutputIndex];
        fft.CopyTo(spread, 0);
        for (var position = 0; position <= 1022; position++)
            spread[position] = MathF.Max(spread[position], MathF.Max(spread[position + 1], spread[position + 2]));
        var copy = (float[])spread.Clone();
        for (var position = 0; position < FftResultSize; position++)
        {
            foreach (var offset in SpreadOffsets)
            {
                var previous = _spreadFftOutputs[(_spreadFftOutputIndex - offset) & (HistorySize - 1)];
                previous[position] = MathF.Max(previous[position], copy[position]);
            }
        }
        _spreadFftOutputIndex = (_spreadFftOutputIndex + 1) & (HistorySize - 1);
    }

    private void DoPeakRecognition()
    {
        var fftMinus46 = _fftOutputs[(_fftOutputIndex - 46) & (HistorySize - 1)];
        var spreadMinus49 = _spreadFftOutputs[(_spreadFftOutputIndex - 49) & (HistorySize - 1)];
        for (var bin = 10; bin <= 1014; bin++)
        {
            if (fftMinus46[bin] < 1f / 64f || fftMinus46[bin] < spreadMinus49[bin - 1]) continue;
            var maxNeighbor = 0f;
            foreach (var offset in FrequencyOffsets)
                maxNeighbor = MathF.Max(maxNeighbor, spreadMinus49[bin + offset]);
            if (fftMinus46[bin] <= maxNeighbor) continue;
            foreach (var offset in TimeOffsets)
            {
                var other = _spreadFftOutputs[(_spreadFftOutputIndex + offset) & (HistorySize - 1)];
                maxNeighbor = MathF.Max(maxNeighbor, other[bin - 1]);
            }
            if (fftMinus46[bin] <= maxNeighbor) continue;
            var magnitude = CalculateMagnitude(fftMinus46[bin]);
            var before = CalculateMagnitude(fftMinus46[bin - 1]);
            var after = CalculateMagnitude(fftMinus46[bin + 1]);
            var variation1 = magnitude * 2f - before - after;
            if (variation1 <= 0f) continue;
            var correctedBinValue = bin * 64 + (int)((after - before) * 32f / variation1);
            if (correctedBinValue is < 0 or > ushort.MaxValue) continue;
            var correctedBin = (ushort)correctedBinValue;
            var frequencyHz = correctedBin * (16000f / 2f / 1024f / 64f);
            var band = GetBand(frequencyHz);
            if (band is null) continue;
            _signature.PeaksByBand[(int)band.Value].Add(new FrequencyPeak(
                _spreadFftsDone - 46,
                (ushort)Math.Clamp((int)magnitude, ushort.MinValue, ushort.MaxValue),
                correctedBin));
        }
    }

    private static float CalculateMagnitude(float value) =>
        MathF.Max(MathF.Log(value), 1f / 64f) * 1477.3f + 6144f;

    private static FrequencyBand? GetBand(float frequencyHz) => frequencyHz switch
    {
        >= 250 and < 520 => FrequencyBand.Hz250To520,
        >= 520 and < 1450 => FrequencyBand.Hz520To1450,
        >= 1450 and < 3500 => FrequencyBand.Hz1450To3500,
        >= 3500 and <= 5500 => FrequencyBand.Hz3500To5500,
        _ => null,
    };

    private static float[] CreateHanningWindow()
    {
        var result = new float[FftSize];
        for (var index = 0; index < result.Length; index++)
            result[index] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * (index + 1) / 2049));
        return result;
    }

    private static float[][] CreateHistory() =>
        Enumerable.Range(0, HistorySize).Select(_ => new float[FftResultSize]).ToArray();
}
