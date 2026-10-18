using Microsoft.EntityFrameworkCore;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Assemblies;

/// <summary>
/// This codebase's first use of a Postgres advisory lock (research.md R3) — serializes structural BOM
/// writes so FR-007a's no-cycle guarantee holds even when two saves to
/// *different* assemblies commit concurrently. Must be called inside an already-open transaction
/// (<c>pg_advisory_xact_lock</c> releases automatically at commit/rollback).
/// </summary>
public static class StructuralLockExtensions
{
    public static Task AcquireStructuralLockAsync(this OpenFarmDbContext dbContext, CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext('bom-structure'))",
            cancellationToken);
}
