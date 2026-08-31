using CircleToSearch.Capture;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class OverlayInteractionStateTests
{
    [Theory]
    [InlineData((int)OverlayInteractionMode.Selecting, (int)OverlayInteractionMode.Listening)]
    [InlineData((int)OverlayInteractionMode.Listening, (int)OverlayInteractionMode.MusicResult)]
    [InlineData((int)OverlayInteractionMode.MusicResult, (int)OverlayInteractionMode.Selecting)]
    [InlineData((int)OverlayInteractionMode.MusicResult, (int)OverlayInteractionMode.Listening)]
    public void Allows_feature_transitions(int sourceValue, int targetValue)
    {
        var source = (OverlayInteractionMode)sourceValue;
        var target = (OverlayInteractionMode)targetValue;
        var state = InMode(source);

        Assert.True(state.TransitionTo(target));
        Assert.Equal(target, state.Mode);
    }

    [Theory]
    [InlineData((int)OverlayInteractionMode.Selecting)]
    [InlineData((int)OverlayInteractionMode.Listening)]
    [InlineData((int)OverlayInteractionMode.MusicResult)]
    public void Every_unfinished_mode_can_close(int sourceValue)
    {
        var source = (OverlayInteractionMode)sourceValue;
        var state = InMode(source);

        Assert.True(state.TransitionTo(OverlayInteractionMode.Closing));
        Assert.True(state.IsFinished);
        Assert.False(state.CanAcceptSelectionInput);
    }

    [Fact]
    public void Repeating_current_transition_is_a_no_op()
    {
        var state = new OverlayInteractionState();

        Assert.False(state.TransitionTo(OverlayInteractionMode.Selecting));
        state.TransitionTo(OverlayInteractionMode.Closing);
        Assert.False(state.TransitionTo(OverlayInteractionMode.Closing));
    }

    [Theory]
    [InlineData((int)OverlayInteractionMode.Selecting, (int)OverlayInteractionMode.MusicResult)]
    [InlineData((int)OverlayInteractionMode.Listening, (int)OverlayInteractionMode.Selecting)]
    [InlineData((int)OverlayInteractionMode.Closing, (int)OverlayInteractionMode.Selecting)]
    [InlineData((int)OverlayInteractionMode.Closing, (int)OverlayInteractionMode.Listening)]
    [InlineData((int)OverlayInteractionMode.Closing, (int)OverlayInteractionMode.MusicResult)]
    public void Rejects_invalid_transitions(int sourceValue, int targetValue)
    {
        var source = (OverlayInteractionMode)sourceValue;
        var target = (OverlayInteractionMode)targetValue;
        var state = InMode(source);

        Assert.Throws<InvalidOperationException>(() => state.TransitionTo(target));
    }

    [Fact]
    public void Selection_input_is_available_only_while_selecting()
    {
        var state = new OverlayInteractionState();
        Assert.True(state.CanAcceptSelectionInput);

        state.TransitionTo(OverlayInteractionMode.Listening);
        Assert.False(state.CanAcceptSelectionInput);
        state.TransitionTo(OverlayInteractionMode.MusicResult);
        Assert.False(state.CanAcceptSelectionInput);
        state.TransitionTo(OverlayInteractionMode.Selecting);
        Assert.True(state.CanAcceptSelectionInput);
    }

    private static OverlayInteractionState InMode(OverlayInteractionMode mode)
    {
        var state = new OverlayInteractionState();
        switch (mode)
        {
            case OverlayInteractionMode.Selecting:
                break;
            case OverlayInteractionMode.Listening:
                state.TransitionTo(OverlayInteractionMode.Listening);
                break;
            case OverlayInteractionMode.MusicResult:
                state.TransitionTo(OverlayInteractionMode.Listening);
                state.TransitionTo(OverlayInteractionMode.MusicResult);
                break;
            case OverlayInteractionMode.Closing:
                state.TransitionTo(OverlayInteractionMode.Closing);
                break;
        }
        return state;
    }
}
