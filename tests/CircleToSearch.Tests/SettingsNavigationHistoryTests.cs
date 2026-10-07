using CircleToSearch.Shell.SettingsPreview;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class SettingsNavigationHistoryTests
{
    [Fact]
    public void Back_and_forward_retrace_visits_and_a_new_visit_drops_the_forward_pages()
    {
        var history = new SettingsNavigationHistory("hotkeys");
        Assert.Null(history.GoBack());
        Assert.Null(history.GoForward());

        history.Visit("general");
        history.Visit("general");
        history.Visit("search");
        Assert.Equal("general", history.GoBack());
        Assert.Equal("hotkeys", history.GoBack());
        Assert.Null(history.GoBack());
        Assert.Equal("hotkeys", history.Current);

        Assert.Equal("general", history.GoForward());
        // Selecting the page a step landed on must not record it as a new visit.
        history.Visit("general");
        Assert.Equal("search", history.GoForward());
        Assert.Null(history.GoForward());

        history.GoBack();
        history.Visit("music");
        Assert.Null(history.GoForward());
        Assert.Equal("general", history.GoBack());
    }

    [Fact]
    public void Only_the_most_recent_pages_are_kept()
    {
        var history = new SettingsNavigationHistory("page0");
        for (var index = 1; index <= 40; index++) history.Visit("page" + index);

        var steps = 0;
        while (history.GoBack() is not null) steps++;

        Assert.Equal(32, steps);
        Assert.Equal("page8", history.Current);
    }
}
