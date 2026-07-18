using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

public interface IFilamentRepository
{
    Task AddAsync(Filament filament, CancellationToken cancellationToken);

    Task<Filament?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Filament>> ListAsync(CancellationToken cancellationToken);

    Task<FilamentVariant?> FindVariantByIdAsync(Guid filamentVariantId, CancellationToken cancellationToken);

    /// <summary>
    /// Explicitly tracks a new <see cref="FilamentVariant"/> as Added and persists it. A variant
    /// appended only to an already-tracked <see cref="Filament"/>'s <see cref="Filament.Variants"/>
    /// collection (rather than added this way) is misdetected as Modified by EF Core's graph-based
    /// change tracking, since its client-generated Guid key is already non-default when discovered —
    /// the well-known "new entity with a pre-set key added via a tracked navigation" pitfall.
    /// </summary>
    Task AddVariantAsync(FilamentVariant variant, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
