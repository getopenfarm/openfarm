using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public interface IExternalPrintRepository
{
    Task AddAsync(ExternalPrint print, CancellationToken cancellationToken);

    /// <summary>The printer's print that has started but not yet ended, with its filaments loaded.</summary>
    Task<ExternalPrint?> GetOpenByPrinterIdAsync(Guid printerId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
