// SPDX-License-Identifier: GPL-3.0-or-later

namespace CircleToSearch.MusicRecognition.Fingerprinting;

public static class Crc32
{
    private const uint Polynomial = 0xEDB88320;

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                var mask = 0u - (crc & 1u);
                crc = (crc >> 1) ^ (Polynomial & mask);
            }
        }
        return ~crc;
    }
}
