using OpenFarm.Domain.ConnectionSecrets;

namespace OpenFarm.Application.ConnectionSecrets;

public interface IConnectionSecretRepository
{
    Task AddAsync(ConnectionSecret connectionSecret, CancellationToken cancellationToken);


    /// <summary>
    /// Natural-key lookup by printer serial — how a printer's connector resolves its LAN
    /// access code without a <c>Printers.ConnectionSecretId</c> foreign key (data-model.md).
    /// </summary>
    Task<ConnectionSecret?> GetByPrinterSerialNumberAsync(string printerSerialNumber, CancellationToken cancellationToken);
}
