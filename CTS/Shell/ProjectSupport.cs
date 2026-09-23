namespace CircleToSearch.Shell;

internal sealed class ProjectSupport(UrlOpeningService urlOpening)
{
    internal const string ProjectUrl = "https://ko-fi.com/keekys";
    internal const string RepositoryUrl = "https://github.com/keekyslusus/CircleFlow";
    internal const string FeedbackUrl = RepositoryUrl + "/issues";
    internal const string LicenseUrl = RepositoryUrl + "/blob/master/LICENSE";

    public void Open()
    {
        urlOpening.TryOpen(ProjectUrl);
    }

    public void OpenRepository() => urlOpening.TryOpen(RepositoryUrl);

    public void OpenFeedback() => urlOpening.TryOpen(FeedbackUrl);

    public void OpenLicense() => urlOpening.TryOpen(LicenseUrl);
}
