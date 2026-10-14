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

    public async Task<IReadOnlyList<MachineProfile>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.MachineProfiles.AsNoTracking().OrderBy(p => p.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MachineProfile>> FindByNameAsync(
        ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken) =>
        await dbContext.MachineProfiles
            .AsNoTracking()
            .Where(p => p.SourceFormat == sourceFormat && p.Name == name)
            .ToListAsync(cancellationToken);

    public Task<MachineProfile?> FindByExternalIdAsync(ProfileSourceFormat sourceFormat, string vendorName, string externalId, CancellationToken cancellationToken) =>
        dbContext.MachineProfiles.FirstOrDefaultAsync(
            p => p.SourceFormat == sourceFormat && p.Origin == ProfileOrigin.System && p.VendorName == vendorName && p.ExternalId == externalId, cancellationToken);

    public Task<MachineProfile?> FindSystemByNameAsync(ProfileSourceFormat sourceFormat, string vendorName, string name, CancellationToken cancellationToken) =>
        dbContext.MachineProfiles.FirstOrDefaultAsync(
            p => p.SourceFormat == sourceFormat && p.Origin == ProfileOrigin.System && p.VendorName == vendorName && p.ExternalId == null && p.Name == name, cancellationToken);

    public async Task<(IReadOnlyList<MachineProfile> Items, int TotalCount)> SearchAsync(
        ProfileOrigin? origin,
        bool? isSelectable,
        string? compatibleWithMachineName,
        string? materialType,
        string? searchText,
        int page,
        int pageSize,
        CancellationToken cancellationToken, OpenFarm.Application.Common.ListSort? sorting = null)
    {
        // compatibleWithMachineName/materialType have no matching column on MachineProfile — a
        // machine profile IS a machine, so neither filter applies here (research.md §6).
        var query = dbContext.MachineProfiles.AsNoTracking().AsQueryable();

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

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
