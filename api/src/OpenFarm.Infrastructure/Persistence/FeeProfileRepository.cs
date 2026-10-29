using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Settings;
using OpenFarm.Domain.Settings;

namespace OpenFarm.Infrastructure.Persistence;

public sealed class FeeProfileRepository(OpenFarmDbContext db) : IFeeProfileRepository
{
    public async Task<IReadOnlyList<FeeProfile>> ListAsync(CancellationToken ct) =>
        await db.FeeProfiles.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);

    public async Task ReplaceAllAsync(IReadOnlyList<FeeProfile> profiles, CancellationToken ct)
    {
        // Delete first in its own statement so a kept name never collides with the unique index.
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await LockAsync(ct);
        await db.FeeProfiles.ExecuteDeleteAsync(ct);
        db.FeeProfiles.AddRange(profiles);
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }

    public async Task<FeeProfileSaveResult> SaveAsync(Guid? id, string name, IReadOnlyList<Fee> fees, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        var profile = id.HasValue ? await db.FeeProfiles.FindAsync([id.Value], ct) : null;
        if (id.HasValue && profile is null) return new(null);
        var otherNames = await db.FeeProfiles.Where(p => p.Id != (id ?? Guid.Empty)).Select(p => p.Name).ToListAsync(ct);
        if (otherNames.Any(n => string.Equals(n, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            return new(null, DuplicateName: true);
        if (profile is null)
        {
            profile = FeeProfile.Create(name, fees);
            db.FeeProfiles.Add(profile);
        }
        else profile.Edit(name, fees);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(profile);
    }

    public async Task<bool> RemoveAsync(Guid id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        var removed = await db.FeeProfiles.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        return removed > 0;
    }

    // Serialize profile mutations, including legacy collection replacement, so case-insensitive
    // name checks and writes remain atomic without changing the existing schema.
    private Task<int> LockAsync(CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(716394281)", ct);
}
