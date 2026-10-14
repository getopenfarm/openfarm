using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.Tests.Profiles.Fakes;

public sealed class FakeFilamentRepository : IFilamentRepository
{
    private readonly List<Filament> _filaments = [];

    public Task AddAsync(Filament filament, CancellationToken cancellationToken)
    {
        _filaments.Add(filament);
        return Task.CompletedTask;
    }

    public Task<Filament?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_filaments.FirstOrDefault(f => f.Id == id));

    public Task<IReadOnlyList<Filament>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Filament>>(_filaments.ToList());

    public Task<FilamentVariant?> FindVariantByIdAsync(Guid filamentVariantId, CancellationToken cancellationToken) =>
        Task.FromResult(_filaments.SelectMany(f => f.Variants).FirstOrDefault(v => v.Id == filamentVariantId));

    // The variant already lives on its parent Filament.Variants (Filament.AddVariant appends it
    // in-memory) — this only mirrors the real repository's explicit-tracking seam, a no-op here.

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
