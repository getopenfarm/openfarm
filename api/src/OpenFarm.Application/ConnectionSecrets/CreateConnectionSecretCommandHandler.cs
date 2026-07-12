using OpenFarm.Application.Common;
using OpenFarm.Domain.ConnectionSecrets;
using OpenFarm.Domain.Organizations;

namespace OpenFarm.Application.ConnectionSecrets;

public sealed class CreateConnectionSecretCommandHandler(
    ISecretProtector secretProtector,
    IConnectionSecretRepository repository) : ICommandHandler<CreateConnectionSecretCommand, ConnectionSecret>
{
    public async Task<ConnectionSecret> HandleAsync(CreateConnectionSecretCommand command, CancellationToken cancellationToken)
    {
        var encryptedAccessCode = secretProtector.Protect(command.PlainAccessCode);
        var connectionSecret = ConnectionSecret.Create(Organization.WellKnownId, command.PrinterSerialNumber, encryptedAccessCode);

        await repository.AddAsync(connectionSecret, cancellationToken);

        return connectionSecret;
    }
}
