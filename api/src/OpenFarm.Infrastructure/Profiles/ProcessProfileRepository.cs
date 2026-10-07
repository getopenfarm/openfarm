using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Profiles;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Profiles;

public sealed class ProcessProfileRepository(OpenFarmDbContext dbContext) : IProcessProfileRepository
{
    public async Task AddAsync(ProcessProfile profile, CancellationToken cancellationToken)
    {
        dbContext.ProcessProfiles.Add(profile);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<ProcessProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ProcessProfiles.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ProcessProfile>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.ProcessProfiles.AsNoTracking().OrderBy(p => p.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProcessProfile>> FindByNameAsync(
        ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken) =>
        await dbContext.ProcessProfiles
            .AsNoTracking()
            .Where(p => p.SourceFormat == sourceFormat && p.Name == name)
            .ToListAsync(cancellationToken);

    public Task<ProcessProfile?> FindByExternalIdAsync(ProfileSourceFormat sourceFormat, string vendorName, string externalId, CancellationToken cancellationToken) =>
        dbContext.ProcessProfiles.FirstOrDefaultAsync(
            p => p.SourceFormat == sourceFormat && p.Origin == ProfileOrigin.System && p.VendorName == vendorName && p.ExternalId == externalId, cancellationToken);

    public Task<ProcessProfile?> FindSystemByNameAsync(ProfileSourceFormat sourceFormat, string vendorName, string name, CancellationToken cancellationToken) =>
        dbContext.ProcessProfiles.FirstOrDefaultAsync(
            p => p.SourceFormat == sourceFormat && p.Origin == ProfileOrigin.System && p.VendorName == vendorName && p.ExternalId == null && p.Name == name, cancellationToken);

    public async Task<(IReadOnlyList<ProcessProfile> Items, int TotalCount)> SearchAsync(
        ProfileOrigin? origin,
        bool? isSelectable,
        string? compatibleWithMachineName,
        string? materialType,
        string? searchText,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        // materialType has no matching column on ProcessProfile — ignored (research.md §6).
        var query = dbContext.ProcessProfiles.AsNoTracking().AsQueryable();

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
            candidates = candidates
                .Where(p => p.CompatibleMachineNames is not null && p.CompatibleMachineNames.Contains(compatibleWithMachineName, StringComparer.Ordinal))
                .ToList();
        }

        var totalCount = candidates.Count;
        var items = candidates.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return (items, totalCount);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
