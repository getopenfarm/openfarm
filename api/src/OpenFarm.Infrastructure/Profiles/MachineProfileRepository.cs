using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Profiles;
using OpenFarm.Infrastructure.Persistence;

#pragma warning disable CA1304, CA1311 // SQL LOWER uses the database collation; ToLowerInvariant is not SQL-translatable.

namespace OpenFarm.Infrastructure.Profiles;

public sealed class MachineProfileRepository(OpenFarmDbContext dbContext) : IMachineProfileRepository
{
    public async Task AddAsync(MachineProfile profile, CancellationToken cancellationToken)
    {
        dbContext.MachineProfiles.Add(profile);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<MachineProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.MachineProfiles.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<MachineProfile>> ListAsync(CancellationToken cancellationToken, ProfileCatalogScope? scope = null)
    {
        var query = dbContext.MachineProfiles.AsNoTracking();
        if (scope is not null)
        {
            query = query.Where(scope.Predicate<MachineProfile>());
        }

        return await query.OrderBy(p => p.Name).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MachineProfile>> FindByNameInCatalogAsync(
        ProfileSourceFormat sourceFormat, string? sourceVersion, string name, CancellationToken cancellationToken) =>
        await dbContext.MachineProfiles
            .AsNoTracking()
            .Where(p => p.SourceFormat == sourceFormat && p.Name == name
                && (p.Origin == ProfileOrigin.User || p.SourceVersion == sourceVersion))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MachineProfile>> FindByNameAnyVersionAsync(
        ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken) =>
        await dbContext.MachineProfiles
            .AsNoTracking()
            .Where(p => p.SourceFormat == sourceFormat && p.Name == name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MachineProfile>> FindInCatalogByIdentityAsync(
        ProfileSourceFormat sourceFormat, string sourceVersion, string? externalId, string? vendorName, string name, CancellationToken cancellationToken) =>
        await dbContext.MachineProfiles
            .AsNoTracking()
            .Where(p => p.SourceFormat == sourceFormat && p.Origin == ProfileOrigin.System && p.SourceVersion == sourceVersion
                && ((externalId != null && p.ExternalId == externalId) || (p.VendorName == vendorName && p.Name == name)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MachineProfile>> ListSystemCatalogAsync(ProfileSourceFormat sourceFormat, string? sourceVersion, CancellationToken cancellationToken) =>
        await dbContext.MachineProfiles
            .Where(p => p.SourceFormat == sourceFormat && p.Origin == ProfileOrigin.System && p.SourceVersion == sourceVersion)
            .ToListAsync(cancellationToken);

    public void Stage(MachineProfile profile) => dbContext.MachineProfiles.Add(profile);

    public async Task<(IReadOnlyList<MachineProfile> Items, int TotalCount)> SearchAsync(
        ProfileOrigin? origin,
        bool? isSelectable,
        string? compatibleWithMachineName,
        string? materialType,
        string? searchText,
        int page,
        int pageSize,
        CancellationToken cancellationToken, OpenFarm.Application.Common.ListSort? sorting = null, ProfileCatalogScope? catalog = null)
    {
        // compatibleWithMachineName/materialType have no matching column on MachineProfile — a
        // machine profile IS a machine, so neither filter applies here (research.md §6).
        var query = dbContext.MachineProfiles.AsNoTracking().AsQueryable();

        if (catalog is not null)
        {
            query = query.Where(catalog.Predicate<MachineProfile>());
        }

        if (origin is { } requestedOrigin)
        {
            query = query.Where(p => p.Origin == requestedOrigin);
        }

        query = ApplySelectabilityGate(query, isSelectable);

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            query = query.Where(p => EF.Functions.ILike(p.Name, $"%{searchText}%"));
        }

        var ordered = (sorting?.Descending == true ? query.OrderByDescending(p => p.Name.ToLower())
            : query.OrderBy(p => p.Name.ToLower())).ThenBy(p => p.Id);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    private static IQueryable<MachineProfile> ApplySelectabilityGate(IQueryable<MachineProfile> query, bool? isSelectable) =>
        isSelectable switch
        {
            true => query.Where(p => (p.IsSelectable && p.IsCurrentlyOffered) || p.Origin == ProfileOrigin.User),
            false => query.Where(p => !((p.IsSelectable && p.IsCurrentlyOffered) || p.Origin == ProfileOrigin.User)),
            null => query,
        };

    public async Task<IReadOnlyList<string>> ListIngestedVersionsAsync(ProfileSourceFormat sourceFormat, CancellationToken cancellationToken) =>
        await dbContext.MachineProfiles
            .AsNoTracking()
            .Where(p => p.SourceFormat == sourceFormat && p.Origin == ProfileOrigin.System && p.SourceVersion != null)
            .Select(p => p.SourceVersion!)
            .Distinct()
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
