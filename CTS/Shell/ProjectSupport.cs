namespace CircleToSearch.Shell;

internal sealed class ProjectSupport(UrlOpeningService urlOpening)
{
    internal const string ProjectUrl = "https://ko-fi.com/keekys";

    public void Open()
    {
        urlOpening.TryOpen(ProjectUrl);
    }
}
