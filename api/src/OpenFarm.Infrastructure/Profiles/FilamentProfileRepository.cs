using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Profiles;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Profiles;

public sealed class FilamentProfileRepository(OpenFarmDbContext dbContext) : IFilamentProfileRepository
{
    public async Task AddAsync(FilamentProfile profile, CancellationToken cancellationToken)
    {
        dbContext.FilamentProfiles.Add(profile);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<FilamentProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.FilamentProfiles.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<FilamentProfile>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.FilamentProfiles.AsNoTracking().OrderBy(p => p.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FilamentProfile>> FindByNameAsync(
        ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken) =>
        await dbContext.FilamentProfiles
            .AsNoTracking()
            .Where(p => p.SourceFormat == sourceFormat && p.Name == name)
            .ToListAsync(cancellationToken);

    public Task<FilamentProfile?> FindByExternalIdAsync(string vendorName, string externalId, CancellationToken cancellationToken) =>
        dbContext.FilamentProfiles.FirstOrDefaultAsync(
            p => p.Origin == ProfileOrigin.System && p.VendorName == vendorName && p.ExternalId == externalId, cancellationToken);

    public Task<FilamentProfile?> FindSystemByNameAsync(string vendorName, string name, CancellationToken cancellationToken) =>
        dbContext.FilamentProfiles.FirstOrDefaultAsync(
            p => p.Origin == ProfileOrigin.System && p.VendorName == vendorName && p.ExternalId == null && p.Name == name, cancellationToken);

    public async Task<(IReadOnlyList<FilamentProfile> Items, int TotalCount)> SearchAsync(
        ProfileOrigin? origin,
        bool? isSelectable,
        string? compatibleWithMachineName,
        string? materialType,
        string? searchText,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = dbContext.FilamentProfiles.AsNoTracking().AsQueryable();

        if (origin is { } requestedOrigin)
        {
            query = query.Where(p => p.Origin == requestedOrigin);
        }

        query = isSelectable switch
        {
            true => query.Where(p => (p.IsSelectable && p.IsCurrentlyOffered) || p.Origin == ProfileOrigin.User),
            false => query.Where(p => !((p.IsSelectable && p.IsCurrentlyOffered) || p.Origin == ProfileOrigin.User)),
            null => query,
        };

        if (!string.IsNullOrWhiteSpace(materialType))
        {
            query = query.Where(p => p.MaterialType == materialType);
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            query = query.Where(p => EF.Functions.ILike(p.Name, $"%{searchText}%"));
        }

        // CompatibleMachineNames is JSONB via a value converter — not translatable to SQL, so the
        // remaining filter and pagination run in memory over the DB-narrowed candidate set
        // (research.md §6's documented tradeoff).
        var candidates = await query.OrderBy(p => p.Name).ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(compatibleWithMachineName))
        {
            // Empty/null CompatibleMachineNames means universally compatible for filament's
            // vendor-generic bases (contracts/profiles-api.md), unlike ProcessProfile's gate.
            candidates = candidates
                .Where(p => p.CompatibleMachineNames is null || p.CompatibleMachineNames.Count == 0
                    || p.CompatibleMachineNames.Contains(compatibleWithMachineName, StringComparer.Ordinal))
                .ToList();
        }

        var totalCount = candidates.Count;
        var items = candidates.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return (items, totalCount);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
