using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public interface IPrintObjectManifestReader
{
    Task<PrintObjectManifest> ReadAsync(Part part, CancellationToken cancellationToken);
}
