using System.Windows;

namespace CircleToSearch.Shell.Onboarding;

// Two overlapping elements trade places; both keep their layout space,
// so the surrounding control never changes size mid-interaction.
internal sealed class OnboardingSwapTransition(FrameworkElement first, FrameworkElement second)
{
    private readonly OnboardingFadeTransition _first = new(first, shown: true);
    private readonly OnboardingFadeTransition _second = new(second, shown: false);

    internal bool ShowsSecond => _second.IsShown;

    internal void Show(bool second)
    {
        _first.Show(!second);
        _second.Show(second);
    }
}
