using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.ConnectionSecrets;

namespace OpenFarm.Api.Endpoints.ConnectionSecrets;

[Permission("connection-secrets:list")]
public sealed class ListConnectionSecretsEndpoint(
    OpenFarm.Application.Common.IQueryHandler<ListConnectionSecretsQuery, IReadOnlyList<ConnectionSecretResult>> handler)
    : EndpointWithoutRequest<IReadOnlyList<ConnectionSecretResponse>>
{
    public override void Configure()
    {
        Get("/api/v1/connection-secrets");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var results = await handler.HandleAsync(new ListConnectionSecretsQuery(), ct);

        Response = results
            .Select(r => new ConnectionSecretResponse
            {
                Id = r.Id,
                PrinterSerialNumber = r.PrinterSerialNumber,
                MaskedAccessCode = r.MaskedAccessCode,
                CreatedAtUtc = r.CreatedAtUtc,
            })
            .ToList();
    }
}
