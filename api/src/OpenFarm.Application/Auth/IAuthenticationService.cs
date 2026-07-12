namespace OpenFarm.Application.Auth;

public interface IAuthenticationService
{
    Task<LoginResult?> AuthenticateAsync(string username, string password, CancellationToken cancellationToken);

    Task<WhoAmIResult?> GetUserAsync(Guid userId, CancellationToken cancellationToken);
}
