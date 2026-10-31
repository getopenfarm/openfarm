using OpenFarm.Application.Common;

namespace OpenFarm.Application.Auth;

public sealed record WhoAmIQuery(Guid UserId);

public sealed record WhoAmIResult(Guid UserId, string Username, string Email)
{
    public string? DisplayName { get; init; }
    public bool IsAdministrator { get; init; }
    public string[] Groups { get; init; } = [];
    public string[] Permissions { get; init; } = [];
}

public sealed class WhoAmIQueryHandler(IAuthenticationService authenticationService)
    : IQueryHandler<WhoAmIQuery, WhoAmIResult?>
{
    public Task<WhoAmIResult?> HandleAsync(WhoAmIQuery query, CancellationToken cancellationToken) =>
        authenticationService.GetUserAsync(query.UserId, cancellationToken);
}
