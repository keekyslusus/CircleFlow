// SPDX-License-Identifier: GPL-3.0-or-later
// Adapted and ported to C# from SongRec; modified beginning 2026-08-29.
// Portions copyright Marin Moulinier and SongRec contributors.

namespace CircleToSearch.MusicRecognition.Fingerprinting;

public enum FrequencyBand
{
    Hz250To520,
    Hz520To1450,
    Hz1450To3500,
    Hz3500To5500,
}

public sealed record FrequencyPeak(uint FftPassNumber, ushort PeakMagnitude, ushort CorrectedPeakFrequencyBin);

public sealed class ShazamSignature
{
    public const int RequiredSampleRate = 16_000;

    public ShazamSignature(int numberSamples)
    {
        NumberSamples = numberSamples;
        PeaksByBand = [[], [], [], []];
    }

    public int SampleRateHz => RequiredSampleRate;
    public int NumberSamples { get; }
    public List<FrequencyPeak>[] PeaksByBand { get; }
    public int DurationMilliseconds => NumberSamples * 1000 / SampleRateHz;
    public int PeakCount => PeaksByBand.Sum(peaks => peaks.Count);
}
