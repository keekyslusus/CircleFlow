using NAudio.Wave;

namespace CircleToSearch.MusicRecognition.Audio;

public sealed record CapturedAudio(byte[] Data, WaveFormat Format);
