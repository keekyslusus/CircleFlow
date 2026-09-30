using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;

namespace CircleToSearch.Links;

internal enum ScreenLinkKind { Web, Email, Wifi, Secret, Text }

// Label is what the user sees before acting, so for web links it is the host the browser will really open.
internal sealed record ScreenLink(ScreenLinkKind Kind, string Label, string CopyText, Uri? Target = null)
{
    private const int MaxUrlLength = 2048;
    private const string WifiPrefix = "WIFI:";
    private const string OneTimePasswordScheme = "otpauth";
    // Shorter dotted text is an abbreviation such as i.e., not a site worth offering.
    private const int MinBareHostLength = 4;
    private const string LeadingWrappers = "([{<\"'«“„‘";
    private const string TrailingPunctuation = ".,;:!?\"'»”“’>";
    private static readonly Regex SplitSeparatorGap = new(@"\s+(?=[./:@])|(?<=[./:@])\s+", RegexOptions.CultureInvariant);

    // A bare domain has no scheme to prove it is a link, so its TLD must be one people type without one.
    // .zip and .mov are left out on purpose: they are file names far more often than sites.
    private static readonly HashSet<string> BareDomainTlds = new(
        ("com net org edu gov mil int info biz name pro app dev page xyz online site website store shop tech " +
         "blog cloud news live art club top link wiki space design media studio games " +
         "ac ad ae af ag ai al am ao aq ar as at au aw ax az ba bb bd be bf bg bh bi bj bm bn bo br bs bt bw by " +
         "bz ca cc cd cf cg ch ci ck cl cm cn co cr cu cv cw cx cy cz de dj dk dm do dz ec ee eg er es et eu fi " +
         "fj fk fm fo fr ga gd ge gf gg gh gi gl gm gn gp gq gr gs gt gu gw gy hk hm hn hr ht hu id ie il im in " +
         "io iq ir is it je jm jo jp ke kg kh ki km kn kp kr kw ky kz la lb lc li lk lr ls lt lu lv ly ma mc md " +
         "me mg mh mk ml mm mn mo mp mq mr ms mt mu mv mw mx my mz na nc ne nf ng ni nl no np nr nu nz om pa pe " +
         "pf pg ph pk pl pm pn pr ps pt pw py qa re ro rs ru rw sa sb sc sd se sg sh si sk sl sm sn so sr ss st " +
         "su sv sx sy sz tc td tf tg th tj tk tl tm tn to tr tt tv tw tz ua ug uk us uy uz va vc ve vg vi vn vu " +
         "wf ws ye yt za zm zw " +
         // рф, рус, укр, бел, қаз
         "xn--p1ai xn--p1acf xn--j1amh xn--90ais xn--80ao21a").Split(' '),
        StringComparer.Ordinal);

    // Country codes that are also common file extensions, such as README.md, libc.so or Makefile.in; they count as
    // a bare domain only with a path, which a file name does not have.
    private static readonly HashSet<string> FileExtensionTlds = new(
        ["cc", "in", "md", "pm", "ps", "py", "rs", "sh", "so"],
        StringComparer.Ordinal);

    internal static ScreenLink Classify(string content)
    {
        var text = content.Trim();
        return Web(text) ?? Email(text) ?? Wifi(text) ?? OneTimePassword(text)
            ?? new ScreenLink(ScreenLinkKind.Text, text, text);
    }

    // OCR text is a link only when the whole selection is one: a sentence that merely mentions a link is searched.
    // OCR splits a link into words and leaves punctuation around it, so only those gaps are undone; look-alike
    // characters such as 1/l or 0/o are never corrected, because that could open someone else's site.
    internal static ScreenLink? FromRecognizedText(string text)
    {
        var trimmed = text.Trim();
        var joined = SplitSeparatorGap.Replace(trimmed, "");
        if (joined.Length == 0 || joined.Length > MaxUrlLength || joined.Any(char.IsWhiteSpace)) return null;
        var candidate = TrimPunctuation(joined);
        if (candidate.Length == 0) return null;
        var rejoined = joined.Length != trimmed.Length;
        return Web(candidate) ?? Email(candidate) ?? BareEmail(candidate) ?? BareDomain(candidate, rejoined);
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
        return WebLink(uri, text);
    }

    // IdnHost is punycode for non-ASCII hosts, so a look-alike such as a Cyrillic "а" cannot pass for a real site.
    private static ScreenLink WebLink(Uri uri, string text)
    {
        var host = uri.IdnHost.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        return new ScreenLink(ScreenLinkKind.Web, host, text, uri);
    }

    private static ScreenLink? BareDomain(string text, bool rejoined)
    {
        if (!Uri.TryCreate("https://" + text, UriKind.Absolute, out var uri) ||
            uri.HostNameType != UriHostNameType.Dns || uri.UserInfo.Length != 0)
            return null;
        var host = uri.IdnHost.ToLowerInvariant();
        var tld = host[(host.LastIndexOf('.') + 1)..];
        if (host.Length < MinBareHostLength || tld.Length == host.Length || !BareDomainTlds.Contains(tld)) return null;
        var hasPath = text.IndexOfAny(['/', '?', '#']) >= 0;
        if (!hasPath && FileExtensionTlds.Contains(tld)) return null;
        // A sentence break such as "Hello. World" rejoins into a dotted word too; a split domain keeps its case.
        if (rejoined && !hasPath && text.Any(char.IsUpper)) return null;
        return WebLink(uri, text);
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

    private static ScreenLink? BareEmail(string text)
    {
        if (!MailAddress.TryCreate(text, out var mail) || mail.Address != text || mail.DisplayName.Length != 0 ||
            !Uri.TryCreate("https://" + mail.Host, UriKind.Absolute, out var domain) ||
            domain.HostNameType != UriHostNameType.Dns || !HasTldShape(domain.IdnHost) ||
            !Uri.TryCreate("mailto:" + text, UriKind.Absolute, out var target))
            return null;
        return new ScreenLink(ScreenLinkKind.Email, text, text, target);
    }

    // The @ already marks an address, so any alphabetic TLD will do; only intranet names such as admin@server fail.
    private static bool HasTldShape(string host)
    {
        var dot = host.LastIndexOf('.');
        if (dot <= 0) return false;
        var tld = host[(dot + 1)..];
        return tld.StartsWith("xn--", StringComparison.OrdinalIgnoreCase) ||
            tld.Length >= 2 && tld.All(char.IsAsciiLetter);
    }

    // Brackets that belong to the link, as in wiki/Foo_(bar), are balanced and stay.
    private static string TrimPunctuation(string text)
    {
        var start = 0;
        while (start < text.Length && LeadingWrappers.Contains(text[start])) start++;
        var end = text.Length;
        while (end > start)
        {
            var last = text[end - 1];
            if (TrailingPunctuation.Contains(last) || IsUnbalancedCloser(text.AsSpan(start, end - start), last))
                end--;
            else break;
        }
        return text[start..end];
    }

    private static bool IsUnbalancedCloser(ReadOnlySpan<char> text, char closer)
    {
        char? opener = closer switch { ')' => '(', ']' => '[', '}' => '{', _ => null };
        return opener is { } open && text.Count(open) < text.Count(closer);
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
