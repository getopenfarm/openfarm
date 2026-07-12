namespace OpenFarm.Application.Auth;

public sealed record LoginCommand(string Username, string Password);

public sealed record LoginResult(string Token, DateTimeOffset ExpiresAtUtc, Guid UserId, Guid OrganizationId);
