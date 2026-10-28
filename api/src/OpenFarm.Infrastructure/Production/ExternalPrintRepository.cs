using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Production;

public sealed class ExternalPrintRepository(OpenFarmDbContext dbContext) : IExternalPrintRepository
{
    public Task AddAsync(ExternalPrint print, CancellationToken cancellationToken)
    {
        dbContext.ExternalPrints.Add(print);
        return Task.CompletedTask;
    }

    public Task<ExternalPrint?> GetOpenByPrinterIdAsync(Guid printerId, CancellationToken cancellationToken) =>
        dbContext.ExternalPrints.Include(p => p.Filaments)
            .Where(p => p.PrinterId == printerId && (p.Status == PrintJobStatus.Printing || p.Status == PrintJobStatus.Paused))
            .OrderByDescending(p => p.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public void TrackNewFilaments(IReadOnlyList<PrintFilament> lines) => dbContext.PrintFilaments.AddRange(lines);

    public Task<ExternalPrint?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ExternalPrints.Include(p => p.Filaments).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<ExternalPrint> Prints, int TotalCount, IReadOnlyDictionary<string, int> StatusCounts)> ListAsync(
        int page, int pageSize, string? status, string? search, CancellationToken cancellationToken)
    {
        var query = dbContext.ExternalPrints.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
            query = query.Where(p => EF.Functions.ILike(p.Name, $"%{term}%", "\\"));
        }

        var counts = await query.GroupBy(p => p.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var statusCounts = Enum.GetValues<PrintJobStatus>().ToDictionary(value => value.ToString(), _ => 0);
        foreach (var count in counts) statusCounts[count.Status.ToString()] = count.Count;

        if (string.Equals(status, "InProgress", StringComparison.OrdinalIgnoreCase))
            query = query.Where(p => p.Status == PrintJobStatus.Printing || p.Status == PrintJobStatus.Paused);
        else if (Enum.TryParse<PrintJobStatus>(status, true, out var selected))
            query = query.Where(p => p.Status == selected);
        else
            query = query.Where(p => p.Status != PrintJobStatus.Cancelled);

        var totalCount = await query.CountAsync(cancellationToken);
        var prints = await query.OrderByDescending(p => p.StartedAtUtc).ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (prints, totalCount, statusCounts);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
