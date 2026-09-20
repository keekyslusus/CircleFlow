using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Shazam;
using CircleToSearch.Shell;
using CircleToSearch.Ui;

namespace CircleToSearch.Search;

internal sealed class MusicResultPresenter(
    UrlOpeningService urlOpening,
    IPluginNotifier notifier,
    UiStrings strings)
{
    public void PresentFallback(MusicRecognitionOutcome outcome)
    {
        switch (outcome.Status)
        {
            case MusicRecognitionStatus.Matched when outcome.Recognition is { } match:
                ShowMatch(match);
                break;
            case MusicRecognitionStatus.NoMatch:
                notifier.ShowMessage(strings.PluginTitle, strings.MusicNoMatch);
                break;
            case MusicRecognitionStatus.NoAudio:
                notifier.ShowMessage(strings.PluginTitle, strings.MusicNoAudio);
                break;
            case MusicRecognitionStatus.RateLimited:
                notifier.ShowError(strings.PluginTitle, strings.MusicRateLimited);
                break;
            case MusicRecognitionStatus.DeviceError:
                notifier.ShowError(strings.PluginTitle, strings.MusicDeviceError);
                break;
            case MusicRecognitionStatus.ServiceError:
                notifier.ShowError(strings.PluginTitle, strings.MusicNetworkError);
                break;
        }
    }

    public bool CanOpen(ShazamRecognition recognition) => IsSafeShazamUrl(recognition.ShazamUrl);

    public void Open(ShazamRecognition recognition)
    {
        urlOpening.TryOpen(recognition.ShazamUrl!);
    }

    internal static bool IsSafeShazamUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return false;
        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        return string.Equals(parsed.Host, "shazam.com", StringComparison.OrdinalIgnoreCase) ||
               parsed.Host.EndsWith(".shazam.com", StringComparison.OrdinalIgnoreCase);
    }

    private void ShowMatch(ShazamRecognition match)
    {
        var title = $"{match.Artist} - {match.Title}";
        var subtitle = strings.MusicMatchSubtitle(match.Album, match.Genre);
        if (CanOpen(match))
        {
            notifier.ShowMessageWithButton(title, subtitle, strings.OpenInShazam, () => Open(match));
            return;
        }
        notifier.ShowMessage(title, subtitle);
    }
}
