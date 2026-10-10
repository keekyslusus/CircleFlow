using System.IO;
using NAudio.Wave;

namespace CircleToSearch.Sounds;

internal static class UiSoundLibrary
{
    private static readonly IReadOnlyDictionary<UiSound, (string[] Names, float Gain)> Files =
        new Dictionary<UiSound, (string[] Names, float Gain)>
        {
            [UiSound.Tap] = (["tap"], 1),
            [UiSound.SwitchOn] = (["switch_on"], 1),
            [UiSound.SwitchOff] = (["switch_off"], 1),
            [UiSound.Key] = (["key_1", "key_2", "key_3", "key_4"], 1),
            [UiSound.Found] = (["found"], 1),
            [UiSound.OverlayOpened] = (["overlay_opened"], 1),
            [UiSound.Toast] = (["toast"], 1),
            [UiSound.ToastError] = (["toast_error"], 1),
            // Ticks repeat many times per stroke, so they sit well below the one-off cues.
            [UiSound.Trace] = (["trace_1", "trace_2", "trace_3", "trace_4"], 0.10f),
            [UiSound.SelectionDone] = (["selection_done"], 1),
            [UiSound.QrFound] = (["qr_found"], 0.15f),
            // Like trace ticks, zoom detents repeat through one gesture and stay quiet.
            [UiSound.ZoomIn] = (["zoom_in_1", "zoom_in_2", "zoom_in_3", "zoom_in_4"], 0.12f),
            [UiSound.ZoomOut] = (["zoom_out_1", "zoom_out_2", "zoom_out_3", "zoom_out_4"], 0.12f),
            [UiSound.ZoomLimit] = (["zoom_limit"], 1),
        };

    internal static IReadOnlyDictionary<UiSound, float[][]> Load(string directory) =>
        Files.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Names
                .Select(name => Read(Path.Combine(directory, name + ".wav"), entry.Value.Gain))
                .ToArray());

    private static float[] Read(string path, float gain)
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
        return samples.Select(sample => sample * gain).ToArray();
    }
}
