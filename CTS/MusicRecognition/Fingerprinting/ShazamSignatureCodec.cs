// SPDX-License-Identifier: GPL-3.0-or-later

using System.Buffers.Binary;
using System.IO;

namespace CircleToSearch.MusicRecognition.Fingerprinting;

public static class ShazamSignatureCodec
{
    public const string DataUriPrefix = "data:audio/vnd.shazam.sig;base64,";
    private const uint Magic1 = 0xCAFE2580;
    private const uint Magic2 = 0x94119C00;
    private const uint ContentsTag = 0x40000000;
    private const uint FrequencyBandTag = 0x60030040;
    private const int HeaderSize = 48;

    public static string EncodeToUri(ShazamSignature signature) =>
        DataUriPrefix + Convert.ToBase64String(EncodeToBinary(signature));

    public static byte[] EncodeToBinary(ShazamSignature signature)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Magic1);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(Magic2);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(3u << 27);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write((uint)(signature.NumberSamples + signature.SampleRateHz * 0.24f));
        writer.Write((uint)((15 << 19) + 0x40000));
        writer.Write(ContentsTag);
        writer.Write(0u);

        for (var band = 0; band < signature.PeaksByBand.Length; band++)
        {
            var peaks = signature.PeaksByBand[band];
            if (peaks.Count == 0) continue;
            using var peaksStream = new MemoryStream();
            using var peaksWriter = new BinaryWriter(peaksStream);
            uint previousPass = 0;
            foreach (var peak in peaks)
            {
                if (peak.FftPassNumber < previousPass)
                    throw new InvalidOperationException("Frequency peaks must be ordered by FFT pass.");
                if (peak.FftPassNumber - previousPass >= byte.MaxValue)
                {
                    peaksWriter.Write(byte.MaxValue);
                    peaksWriter.Write(peak.FftPassNumber);
                    previousPass = peak.FftPassNumber;
                }
                peaksWriter.Write((byte)(peak.FftPassNumber - previousPass));
                peaksWriter.Write(peak.PeakMagnitude);
                peaksWriter.Write(peak.CorrectedPeakFrequencyBin);
                previousPass = peak.FftPassNumber;
            }
            var peakBytes = peaksStream.ToArray();
            writer.Write(FrequencyBandTag + (uint)band);
            writer.Write((uint)peakBytes.Length);
            writer.Write(peakBytes);
            for (var padding = peakBytes.Length % 4; padding != 0 && padding < 4; padding++)
                writer.Write((byte)0);
        }

        var buffer = stream.ToArray();
        var sizeWithoutHeader = (uint)(buffer.Length - HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8, 4), sizeWithoutHeader);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(HeaderSize + 4, 4), sizeWithoutHeader);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4, 4), Crc32.Compute(buffer.AsSpan(8)));
        return buffer;
    }

    public static bool HasValidHeader(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < HeaderSize + 8) return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(buffer) != Magic1) return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(buffer[12..]) != Magic2) return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(buffer[8..]) != buffer.Length - HeaderSize) return false;
        return BinaryPrimitives.ReadUInt32LittleEndian(buffer[4..]) == Crc32.Compute(buffer[8..]);
    }
}
