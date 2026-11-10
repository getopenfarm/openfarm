namespace OpenFarm.Infrastructure.Auth.OAuth;

/// <summary>A protocol failure. Only the standard error code is ever returned (<c>{"error":"…"}</c>), never a message.</summary>
public sealed class OAuthException(string code, int status = 400) : Exception(code)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
