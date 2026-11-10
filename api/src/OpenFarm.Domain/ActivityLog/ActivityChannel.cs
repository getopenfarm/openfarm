namespace OpenFarm.Domain.ActivityLog;

/// <summary>The credential an operator acted through when it was not a browser session.</summary>
/// <param name="Kind"><c>Token</c> (personal access token) or <c>App</c> (OAuth connected app).</param>
/// <param name="Label">Token name or app name at the time of the change.</param>
public sealed record ActivityChannel(string Kind, Guid CredentialId, string Label)
{
    public const string Token = "Token";
    public const string App = "App";
}
