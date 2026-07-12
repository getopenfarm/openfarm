using OpenFarm.Application.Common;

namespace OpenFarm.Application.ConnectionSecrets;

public sealed record ListConnectionSecretsQuery;

public sealed record ConnectionSecretResult(Guid Id, string PrinterSerialNumber, string MaskedAccessCode, DateTimeOffset CreatedAtUtc);

public sealed class ListConnectionSecretsQueryHandler(IConnectionSecretRepository repository)
    : IQueryHandler<ListConnectionSecretsQuery, IReadOnlyList<ConnectionSecretResult>>
{
    private const string MaskedValue = "***";

    public async Task<IReadOnlyList<ConnectionSecretResult>> HandleAsync(ListConnectionSecretsQuery query, CancellationToken cancellationToken)
    {
        var secrets = await repository.ListAsync(cancellationToken);

        return secrets
            .Select(s => new ConnectionSecretResult(s.Id, s.PrinterSerialNumber, MaskedValue, s.CreatedAtUtc))
            .ToList();
    }
}
