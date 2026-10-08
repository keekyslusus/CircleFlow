using System.IO;
using NAudio.Wave;

namespace CircleToSearch.Sounds;

internal static class UiSoundLibrary
{
    private static readonly IReadOnlyDictionary<UiSound, string[]> Files = new Dictionary<UiSound, string[]>
    {
        [UiSound.Tap] = ["tap"],
        [UiSound.SwitchOn] = ["switch_on"],
        [UiSound.SwitchOff] = ["switch_off"],
        [UiSound.Key] = ["key_1", "key_2", "key_3", "key_4"],
        [UiSound.Found] = ["found"],
        [UiSound.OverlayOpened] = ["overlay_opened"],
    };

    internal static IReadOnlyDictionary<UiSound, float[][]> Load(string directory) =>
        Files.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Select(name => Read(Path.Combine(directory, name + ".wav"))).ToArray());

    private static float[] Read(string path)
    {
        using var reader = new WaveFileReader(path);
        if (reader.WaveFormat.SampleRate != UiSoundPlayer.Format.SampleRate || reader.WaveFormat.Channels != 1)
            throw new InvalidDataException($"{Path.GetFileName(path)} must be {UiSoundPlayer.Format.SampleRate} Hz mono.");
        var source = reader.ToSampleProvider();
        var samples = new List<float>();
        var buffer = new float[4096];
        int read;
        while ((read = source.Read(buffer.AsSpan())) > 0)
            samples.AddRange(buffer.AsSpan(0, read));
        return samples.ToArray();
    }
}
