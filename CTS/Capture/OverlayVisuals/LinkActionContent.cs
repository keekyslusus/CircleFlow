namespace CircleToSearch.Capture;

using System.Windows.Media;
using CircleToSearch.Links;
using CircleToSearch.Ui;

// Every action that opens a link shows its target the same way, so the user can check it before clicking.
internal static class LinkActionContent
{
    private const int LabelTextElementLimit = 40;

    internal static string Label(ScreenLink link) =>
        link.Kind == ScreenLinkKind.Web
            ? ShortenHost(link.Label)
            : ClipboardCopyService.Preview(link.Label, LabelTextElementLimit);

    internal static Geometry Icon(ScreenLink link) =>
        link.Kind == ScreenLinkKind.Email ? PluginIcons.MailOutlined : PluginIcons.LinkOutlined;

    // The registrable domain sits at the right end of a host, so a long host keeps that end; cutting from the
    // right would let login.microsoft.com.evil.example pass for Microsoft.
    internal static string ShortenHost(string host)
    {
        if (host.Length <= LabelTextElementLimit) return host;
        var tail = host[^LabelTextElementLimit..];
        var boundary = tail.IndexOf('.');
        return "..." + (boundary >= 0 && boundary < tail.Length - 1 ? tail[(boundary + 1)..] : tail);
    }
}
