using System.Windows;

namespace CircleToSearch.Ui.Effects;

public enum SceneRipplePreset
{
    Entrance,
    AudioTransient,
    MusicMatch,
}

public readonly record struct SceneRippleRequest(Point Origin, SceneRipplePreset Preset, double Intensity);

public interface ISceneRippleSink
{
    void Emit(SceneRippleRequest request);
}
