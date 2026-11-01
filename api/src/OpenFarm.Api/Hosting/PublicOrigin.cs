namespace OpenFarm.Api.Hosting;

/// <summary>The optional canonical public address, independent of reverse-proxy request headers.</summary>
public sealed class PublicOrigin
{
    public string? Value { get; }
    public bool IsHttps { get; }

    public PublicOrigin(IConfiguration configuration)
    {
        var value = configuration["ORIGIN"];
        if (string.IsNullOrWhiteSpace(value)) return;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.IsWellFormedOriginalString()
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || value.Contains('?') || value.Contains('#') || value.Contains('@')
            || value.Contains('\\') || value.Any(char.IsWhiteSpace))
        {
            throw new InvalidOperationException("ORIGIN must be an absolute HTTP or HTTPS origin without credentials, a non-root path, query, or fragment (for example https://farm.example.com).");
        }

        var pathStart = value.IndexOf('/', value.IndexOf("://", StringComparison.Ordinal) + 3);
        if (pathStart >= 0 && value[pathStart..] != "/")
            throw new InvalidOperationException("ORIGIN must not contain a non-root path.");

        Value = uri.GetLeftPart(UriPartial.Authority);
        IsHttps = uri.Scheme == Uri.UriSchemeHttps;
    }
}
