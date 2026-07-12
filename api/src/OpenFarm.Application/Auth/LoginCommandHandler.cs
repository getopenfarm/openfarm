using OpenFarm.Application.Common;

namespace OpenFarm.Application.Auth;

public sealed class LoginCommandHandler(IAuthenticationService authenticationService)
    : ICommandHandler<LoginCommand, LoginResult?>
{
    public Task<LoginResult?> HandleAsync(LoginCommand command, CancellationToken cancellationToken) =>
        authenticationService.AuthenticateAsync(command.Username, command.Password, cancellationToken);
}
