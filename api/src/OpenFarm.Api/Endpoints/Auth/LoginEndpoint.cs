using FastEndpoints;
using OpenFarm.Application.Auth;
using LoginHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Auth.LoginCommand, OpenFarm.Application.Auth.LoginResult?>;

namespace OpenFarm.Api.Endpoints.Auth;

public sealed class LoginResponse
{
    public string Token { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; set; }
}

public sealed class LoginEndpoint(LoginHandler handler) : Endpoint<LoginCommand, LoginResponse>
{
    public override void Configure()
    {
        Post("/api/v1/auth/login");
        AllowAnonymous();
    }

    public override async Task HandleAsync(LoginCommand req, CancellationToken ct)
    {
        var result = await handler.HandleAsync(req, ct);

        if (result is null)
        {
            await HttpContext.Response.SendUnauthorizedAsync(ct);
            return;
        }

        Response = new LoginResponse { Token = result.Token, ExpiresAtUtc = result.ExpiresAtUtc };
    }
}
