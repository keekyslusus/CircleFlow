using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace CircleToSearch.MusicRecognition.Audio;

public static class AudioOutputDevice
{
    // Must match the endpoint WasapiRecorderBuilder picks for loopback capture when no device is set.
    public static string? DefaultName()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (!enumerator.TryGetDefaultAudioEndpoint(DataFlow.Render, Role.Console, out var device)) return null;
            using (device) return device.FriendlyName;
        }
        catch (Exception exception) when (exception is COMException or CoreAudioException)
        {
            return null;
        }
    }
}
