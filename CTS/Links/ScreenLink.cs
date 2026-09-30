using System.Net.Mail;
using System.Text;

namespace CircleToSearch.Links;

internal enum ScreenLinkKind { Web, Email, Wifi, Secret, Text }

// Label is what the user sees before acting, so for web links it is the host the browser will really open.
internal sealed record ScreenLink(ScreenLinkKind Kind, string Label, string CopyText, Uri? Target = null)
{
    private const int MaxUrlLength = 2048;
    private const string WifiPrefix = "WIFI:";
    private const string OneTimePasswordScheme = "otpauth";

    internal static ScreenLink Classify(string content)
    {
        var text = content.Trim();
        return Web(text) ?? Email(text) ?? Wifi(text) ?? OneTimePassword(text)
            ?? new ScreenLink(ScreenLinkKind.Text, text, text);
    }

    // Other schemes are copied only: an app URI such as tg://login would sign the scanned session into this PC.
    internal static bool CanOpen(Uri uri) =>
        uri.IsAbsoluteUri && (IsWebScheme(uri.Scheme) || uri.Scheme == Uri.UriSchemeMailto);

    private static ScreenLink? Web(string text)
    {
        if (text.Length > MaxUrlLength || text.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
            return null;
        var candidate = text.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + text : text;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || !IsWebScheme(uri.Scheme) ||
            string.IsNullOrEmpty(uri.IdnHost))
            return null;
        // IdnHost is punycode for non-ASCII hosts, so a look-alike such as a Cyrillic "а" cannot pass for a real site.
        var host = uri.IdnHost.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        return new ScreenLink(ScreenLinkKind.Web, host, text, uri);
    }

    private static ScreenLink? Email(string text)
    {
        if (!text.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(text, UriKind.Absolute, out var uri))
            return null;
        var address = Uri.UnescapeDataString(text["mailto:".Length..].Split('?', 2)[0]);
        if (!MailAddress.TryCreate(address, out var mail) || mail.Address != address) return null;
        return new ScreenLink(ScreenLinkKind.Email, address, address, uri);
    }

    private static ScreenLink? Wifi(string text)
    {
        if (!text.StartsWith(WifiPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        var fields = WifiFields(text[WifiPrefix.Length..]);
        if (!fields.TryGetValue("S", out var network) || network.Length == 0) return null;
        return fields.TryGetValue("P", out var password) && password.Length != 0
            ? new ScreenLink(ScreenLinkKind.Wifi, network, password)
            : new ScreenLink(ScreenLinkKind.Text, network, network);
    }

    // A 2FA setup code carries its secret in the query, so only the account name may be shown.
    private static ScreenLink? OneTimePassword(string text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, OneTimePasswordScheme, StringComparison.OrdinalIgnoreCase))
            return null;
        var account = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
        return new ScreenLink(ScreenLinkKind.Secret, account.Length == 0 ? uri.Host : account, text);
    }

    // Values escape \ ; , : and " with a backslash.
    private static Dictionary<string, string> WifiFields(string body)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var key = new StringBuilder();
        var value = new StringBuilder();
        var inValue = false;
        for (var index = 0; index < body.Length; index++)
        {
            var character = body[index];
            if (inValue && character == '\\' && index + 1 < body.Length)
            {
                value.Append(body[++index]);
            }
            else if (!inValue && character == ':')
            {
                inValue = true;
            }
            else if (character == ';')
            {
                if (inValue) fields.TryAdd(key.ToString(), value.ToString());
                key.Clear();
                value.Clear();
                inValue = false;
            }
            else (inValue ? value : key).Append(character);
        }
        if (inValue) fields.TryAdd(key.ToString(), value.ToString());
        return fields;
    }

    private static bool IsWebScheme(string scheme) => scheme == Uri.UriSchemeHttps || scheme == Uri.UriSchemeHttp;
}
