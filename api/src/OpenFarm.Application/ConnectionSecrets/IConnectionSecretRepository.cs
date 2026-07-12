using OpenFarm.Domain.ConnectionSecrets;

namespace OpenFarm.Application.ConnectionSecrets;

public interface IConnectionSecretRepository
{
    Task AddAsync(ConnectionSecret connectionSecret, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConnectionSecret>> ListAsync(CancellationToken cancellationToken);
}
