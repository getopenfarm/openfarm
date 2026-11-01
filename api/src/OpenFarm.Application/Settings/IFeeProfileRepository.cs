using OpenFarm.Domain.Settings;

namespace OpenFarm.Application.Settings;

public sealed record FeeProfileSaveResult(FeeProfile? Profile, bool DuplicateName = false);

public interface IFeeProfileRepository
{
    Task<IReadOnlyList<FeeProfile>> ListAsync(CancellationToken ct);
    Task ReplaceAllAsync(IReadOnlyList<FeeProfile> profiles, CancellationToken ct);
    Task<FeeProfileSaveResult> SaveAsync(Guid? id, string name, IReadOnlyList<Fee> fees, CancellationToken ct);
    Task<bool> RemoveAsync(Guid id, CancellationToken ct);
}
