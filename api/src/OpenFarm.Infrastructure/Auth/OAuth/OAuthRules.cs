using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Primitives;

namespace OpenFarm.Infrastructure.Auth.OAuth;

/// <summary>Where an app's authorization response goes (dev-notes/MCP.md, redirect URIs).</summary>
public enum RedirectKind
{
    /// <summary><c>https:</c> on any host.</summary>
    Web,

    /// <summary><c>http:</c> on localhost, 127.0.0.1 or [::1]; the port is ignored when matching.</summary>
    Loopback,

    /// <summary>A desktop app's own scheme: exactly <c>cursor</c>, <c>vscode</c> or <c>vscode-insiders</c>.</summary>
    Native,
}

/// <summary>What the consent page and the Connected apps list show under "Returns to".</summary>
/// <param name="Kind"><c>Web</c>, <c>Loopback</c> or <c>Native</c>.</param>
public sealed record ReturnsTo(string Kind, string Label);

/// <summary>The pure protocol checks of the OAuth server. Each one has a named negative test.</summary>
public static partial class OAuthRules
{
    private static readonly string[] LoopbackHosts = ["localhost", "127.0.0.1", "[::1]"];

    // MCP 2025-11-25 asks for localhost or HTTPS redirects only. These native schemes are a recorded
    // deviation (dev-notes/MCP.md): Cursor and VS Code register them. Changing the list is one line + a test.
    private static readonly string[] NativeSchemes = ["cursor", "vscode", "vscode-insiders"];

    // Letters that render as nothing (Hangul fillers, Braille blank), so a name could look empty or padded.
    private static readonly int[] BlankFillers = [0x115F, 0x1160, 0x3164, 0xFFA0, 0x2800];

    [GeneratedRegex("^[A-Za-z0-9_-]{43}$")]
    private static partial Regex ChallengePattern();

    [GeneratedRegex("^[A-Za-z0-9._~-]{43,128}$")]
    private static partial Regex VerifierPattern();

    /// <summary>The kind of a redirect URI an app may register, or null when it may not.</summary>
    public static RedirectKind? ClassifyRedirect(string? uri)
    {
        if (string.IsNullOrEmpty(uri) || uri.Length > 2048) return null;
        // Printable ASCII only, no fragment and no backslash (which Uri would silently turn into '/').
        foreach (var c in uri)
            if (c is <= ' ' or > '~' or '#' or '\\') return null;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || parsed.UserInfo.Length > 0 || Authority(uri).Contains('@')) return null;
        return parsed.Scheme switch
        {
            "https" when parsed.Host.Length > 0 => RedirectKind.Web,
            "http" when LoopbackHosts.Contains(parsed.Host, StringComparer.OrdinalIgnoreCase) => RedirectKind.Loopback,
            _ when NativeSchemes.Contains(parsed.Scheme, StringComparer.Ordinal) => RedirectKind.Native,
            _ => null,
        };
    }

    /// <summary>
    /// Whether a requested redirect URI is the registered one: exactly, or for loopback with any port
    /// (RFC 8252 §7.3; CLIs pick a port per run).
    /// </summary>
    public static bool RedirectMatches(string registered, string requested)
    {
        if (ClassifyRedirect(requested) is not { } kind) return false;
        if (string.Equals(registered, requested, StringComparison.Ordinal)) return true;
        return kind == RedirectKind.Loopback && ClassifyRedirect(registered) == RedirectKind.Loopback
            && string.Equals(WithoutPort(registered), WithoutPort(requested), StringComparison.Ordinal);
    }

    public static ReturnsTo ReturnsTo(string redirectUri)
    {
        var uri = new Uri(redirectUri, UriKind.Absolute);
        return uri.Scheme switch
        {
            "https" => new("Web", uri.IdnHost),
            "http" => new("Loopback", "this computer (localhost)"),
            _ => new("Native", $"an app on this device ({uri.Scheme}:)"),
        };
    }

    public static bool IsValidChallenge(string? challenge) => challenge is not null && ChallengePattern().IsMatch(challenge);

    /// <summary>S256 only: base64url(SHA-256(verifier)) must equal the challenge, compared in constant time.</summary>
    public static bool VerifyPkce(string? verifier, string challenge)
    {
        if (verifier is null || !VerifierPattern().IsMatch(verifier) || !IsValidChallenge(challenge)) return false;
        var computed = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(challenge));
    }

    /// <summary>
    /// A client name is an untrusted label: trimmed, at most 100 characters, free of control, format (zero-width,
    /// bidirectional overrides), line-separator, combining, private-use, unassigned and blank-filler characters,
    /// and not "OpenFarm". Null when refused.
    /// </summary>
    public static string? NormalizeClientName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "MCP client";
        // Composed first, so a decomposed accent (e + U+0301) is one letter, not a refused combining mark.
        string trimmed;
        try { trimmed = name.Trim().Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { return null; } // lone surrogates
        if (trimmed.Length > 100) return null;
        for (var i = 0; i < trimmed.Length;)
        {
            if (Rune.DecodeFromUtf16(trimmed.AsSpan(i), out var rune, out var used) != OperationStatus.Done) return null;
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format
                    or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.NonSpacingMark
                    or UnicodeCategory.EnclosingMark or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned
                || BlankFillers.Contains(rune.Value)) return null;
            i += used;
        }
        // An app may not pass itself off as OpenFarm on OpenFarm's own consent page.
        if (string.Concat(trimmed.Where(c => !char.IsWhiteSpace(c))).Equals("OpenFarm", StringComparison.OrdinalIgnoreCase)) return null;
        return trimmed;
    }

    /// <summary>An absent state is fine; a present one is 1–1024 printable ASCII characters.</summary>
    public static bool ValidState(string? state) =>
        state is null || (state.Length is > 0 and <= 1024 && state.All(c => c is >= ' ' and <= '~'));

    /// <summary>
    /// Protocol parameters, or null when a key repeats or a value is longer than 4096 characters. A parameter
    /// sent without a value is treated as omitted (RFC 6749 §3.1).
    /// </summary>
    public static Dictionary<string, string>? ParseParameters(IEnumerable<KeyValuePair<string, StringValues>> pairs)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, values) in pairs)
        {
            if (values.Count != 1 || values[0] is not { Length: <= 4096 } value || result.ContainsKey(key)) return null;
            if (value.Length > 0) result.Add(key, value);
        }
        return result;
    }

    private static string Authority(string uri)
    {
        var start = uri.IndexOf("://", StringComparison.Ordinal);
        if (start < 0) return "";
        start += 3;
        var end = uri.IndexOfAny(['/', '?'], start);
        return end < 0 ? uri[start..] : uri[start..end];
    }

    private static string WithoutPort(string uri)
    {
        var authority = Authority(uri);
        var host = authority.StartsWith('[') ? authority[..(authority.IndexOf(']') + 1)] : authority.Split(':')[0];
        var rest = uri[(uri.IndexOf("://", StringComparison.Ordinal) + 3 + authority.Length)..];
        return "http://" + host.ToLowerInvariant() + rest;
    }
}
