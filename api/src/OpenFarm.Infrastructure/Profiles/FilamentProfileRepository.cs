using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Profiles;
using OpenFarm.Infrastructure.Persistence;

#pragma warning disable CA1304, CA1311 // SQL LOWER uses the database collation; ToLowerInvariant is not SQL-translatable.

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

    public async Task<IReadOnlyList<FilamentProfile>> ListAsync(CancellationToken cancellationToken, ProfileCatalogScope? scope = null)
    {
        var query = dbContext.FilamentProfiles.AsNoTracking();
        if (scope is not null)
        {
            query = query.Where(scope.Predicate<FilamentProfile>());
        }

        return await query.OrderBy(p => p.Name).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FilamentProfile>> FindByNameInCatalogAsync(
        ProfileSourceFormat sourceFormat, string? sourceVersion, string name, CancellationToken cancellationToken) =>
        await dbContext.FilamentProfiles
            .AsNoTracking()
            .Where(p => p.SourceFormat == sourceFormat && p.Name == name
                && (p.Origin == ProfileOrigin.User || p.SourceVersion == sourceVersion))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FilamentProfile>> FindByNameAnyVersionAsync(
        ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken) =>
        await dbContext.FilamentProfiles
            .AsNoTracking()
            .Where(p => p.SourceFormat == sourceFormat && p.Name == name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FilamentProfile>> FindInCatalogByIdentityAsync(
        ProfileSourceFormat sourceFormat, string sourceVersion, string? externalId, string? vendorName, string name, CancellationToken cancellationToken) =>
        await dbContext.FilamentProfiles
            .AsNoTracking()
            .Where(p => p.SourceFormat == sourceFormat && p.Origin == ProfileOrigin.System && p.SourceVersion == sourceVersion
                && ((externalId != null && p.ExternalId == externalId) || (p.VendorName == vendorName && p.Name == name)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FilamentProfile>> ListSystemCatalogAsync(ProfileSourceFormat sourceFormat, string? sourceVersion, CancellationToken cancellationToken) =>
        await dbContext.FilamentProfiles
            .Where(p => p.SourceFormat == sourceFormat && p.Origin == ProfileOrigin.System && p.SourceVersion == sourceVersion)
            .ToListAsync(cancellationToken);

    public void Stage(FilamentProfile profile) => dbContext.FilamentProfiles.Add(profile);

    public async Task<(IReadOnlyList<FilamentProfile> Items, int TotalCount)> SearchAsync(
        ProfileOrigin? origin,
        bool? isSelectable,
        string? compatibleWithMachineName,
        string? materialType,
        string? searchText,
        int page,
        int pageSize,
        CancellationToken cancellationToken, OpenFarm.Application.Common.ListSort? sorting = null, ProfileCatalogScope? catalog = null)
    {
        var query = dbContext.FilamentProfiles.AsNoTracking().AsQueryable();

        if (catalog is not null)
        {
            query = query.Where(catalog.Predicate<FilamentProfile>());
        }

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
        var ordered = (sorting?.Descending == true ? query.OrderByDescending(p => p.Name.ToLower())
            : query.OrderBy(p => p.Name.ToLower())).ThenBy(p => p.Id);
        var candidates = await ordered.ToListAsync(cancellationToken);

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
