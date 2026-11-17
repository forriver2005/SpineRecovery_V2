using System;
using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// Tracker HTTP requests may target literal private IPv4 addresses only. Do not resolve
/// hostnames: a DNS name, even one that looks local, could resolve to a public endpoint.
/// </summary>
public static class TrackerLanPolicy
{
    private static readonly Regex OriginPattern = new Regex(
        @"\A(?:(https?)://)?((?:0|[1-9][0-9]{0,2})(?:\.(?:0|[1-9][0-9]{0,2})){3})(?::([1-9][0-9]{0,4}))?/?\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool TryCreateMotorUrl(string address, int durationMs, out string url)
    {
        url = null;
        string origin;
        if (!TryOrigin(address, false, out origin)) return false;
        url = origin + "/motor?duration_ms=" +
            Math.Max(1, Math.Min(60000, durationMs)).ToString(CultureInfo.InvariantCulture);
        return true;
    }

    // The external discovery URL is used by the Editor/desktop path only. Permit the
    // local SlimeVR process on 127.0.0.1 as well as a private LAN host.
    public static bool TryDiscoveryUrl(string value, out string url)
    {
        url = null;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string input = value.Trim();
        if (input.IndexOf('\\') >= 0) return false;
        for (int index = 0; index < input.Length; index++)
            if (char.IsWhiteSpace(input[index]) || char.IsControl(input[index])) return false;
        Uri parsed;
        if (!Uri.TryCreate(input, UriKind.Absolute, out parsed) ||
            (parsed.Scheme != "http" && parsed.Scheme != "https") ||
            !string.IsNullOrEmpty(parsed.UserInfo) || !string.IsNullOrEmpty(parsed.Fragment)) return false;
        int authorityStart = input.IndexOf("://", StringComparison.Ordinal);
        if (authorityStart < 0) return false;
        int authorityEnd = input.IndexOfAny(new[] { '/', '?', '#' }, authorityStart + 3);
        string rawOrigin = authorityEnd < 0 ? input : input.Substring(0, authorityEnd);
        string origin;
        if (!TryOrigin(rawOrigin, true, out origin)) return false;
        url = origin + parsed.PathAndQuery;
        return true;
    }

    private static bool TryOrigin(string value, bool allowLoopback, out string origin)
    {
        origin = null;
        if (string.IsNullOrWhiteSpace(value)) return false;
        Match match = OriginPattern.Match(value.Trim());
        if (!match.Success) return false;
        string host = match.Groups[2].Value;
        string[] pieces = host.Split('.');
        int[] octets = new int[4];
        for (int index = 0; index < pieces.Length; index++)
            if (!int.TryParse(pieces[index], NumberStyles.None, CultureInfo.InvariantCulture,
                    out octets[index]) || octets[index] > 255) return false;
        bool isPrivate = octets[0] == 10 ||
            (octets[0] == 172 && octets[1] >= 16 && octets[1] <= 31) ||
            (octets[0] == 192 && octets[1] == 168);
        if (!isPrivate && !(allowLoopback && host == "127.0.0.1")) return false;
        string port = match.Groups[3].Value;
        int portNumber;
        if (port.Length != 0 && (!int.TryParse(port, NumberStyles.None,
                CultureInfo.InvariantCulture, out portNumber) || portNumber > 65535)) return false;
        string scheme = match.Groups[1].Success ? match.Groups[1].Value.ToLowerInvariant() : "http";
        origin = scheme + "://" + host + (port.Length == 0 ? "" : ":" + port);
        return true;
    }
}
