using OpenFarm.Application.Common;

namespace OpenFarm.Application.Parts;

public sealed class GetPartFileQueryHandler(IPartRepository partRepository, IPartFileStorage storage)
    : IQueryHandler<GetPartFileQuery, PartFileDownload?>
{
    public async Task<PartFileDownload?> HandleAsync(GetPartFileQuery query, CancellationToken cancellationToken)
    {
        var part = await partRepository.GetByIdAsync(query.PartId, cancellationToken);
        if (part is null)
        {
            return null;
        }

        var stream = await storage.OpenReadAsync(part.StoragePath, cancellationToken);
        return new PartFileDownload(stream, part.OriginalFileName);
    }
}
