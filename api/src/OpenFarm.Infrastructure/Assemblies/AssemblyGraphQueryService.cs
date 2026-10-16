using System.Data;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Assemblies;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Assemblies;

/// <summary>
/// This codebase's first raw-SQL query (research.md R1) — <see cref="ExplodeAsync"/> runs a literal
/// <c>WITH RECURSIVE</c> CTE over the current ADO.NET connection so BOM decomposition scales with rows
/// scanned, not round-trips-per-level (SC-007). <see cref="GetOrganizationEdgesAsync"/> is a plain,
/// non-recursive LINQ query feeding <see cref="AssemblyGraph"/> (research.md R2).
/// </summary>
public sealed class AssemblyGraphQueryService(OpenFarmDbContext dbContext) : IAssemblyGraphQueryService
{
    private const int MaxDepth = 10;

    public Task<IReadOnlyList<(Guid ParentAssemblyId, Guid ChildAssemblyId)>> GetOrganizationEdgesAsync(
        Guid organizationId, CancellationToken cancellationToken) =>
        GetOrganizationEdgesCoreAsync(organizationId, cancellationToken);

    private async Task<IReadOnlyList<(Guid, Guid)>> GetOrganizationEdgesCoreAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var rows = await dbContext.BomLines
            .Where(l => l.OrganizationId == organizationId && l.TargetKind == BomLineTargetKind.Assembly)
            .Select(l => new { Parent = l.AssemblyId, Child = l.TargetAssemblyId!.Value })
            .ToListAsync(cancellationToken);

        return rows.Select(r => (r.Parent, r.Child)).ToList();
    }

    /// <summary>
    /// FR-009 — a literal <c>WITH RECURSIVE</c> CTE (research.md R1), anchored on the root assembly's
    /// own lines multiplied by the requested quantity, recursing through Assembly-kind lines with a
    /// hard <c>depth &lt; 10</c> termination guard (FR-009f) independent of save-time cycle rejection,
    /// aggregated by leaf target and split into printed parts vs. non-printed components (FR-009d).
    /// Never persists anything.
    /// </summary>
    public async Task<BomExplosionResult> ExplodeAsync(Guid organizationId, Guid rootAssemblyId, int quantity, CancellationToken cancellationToken)
    {
        var sql = $"""
            WITH RECURSIVE explosion AS (
                SELECT bl."TargetKind" AS "TargetKind", bl."TargetPartId" AS "TargetPartId",
                       bl."TargetAssemblyId" AS "TargetAssemblyId", bl."TargetNonPrintedComponentId" AS "TargetNonPrintedComponentId",
                       bl."Quantity" * @quantity AS "Multiplier", 1 AS "Depth"
                FROM "BomLines" bl
                WHERE bl."AssemblyId" = @rootAssemblyId AND bl."OrganizationId" = @organizationId
                UNION ALL
                SELECT child."TargetKind", child."TargetPartId", child."TargetAssemblyId", child."TargetNonPrintedComponentId",
                       child."Quantity" * e."Multiplier", e."Depth" + 1
                FROM "BomLines" child
                JOIN explosion e ON child."AssemblyId" = e."TargetAssemblyId"
                WHERE e."TargetKind" = 'Assembly' AND e."Depth" < {MaxDepth} AND child."OrganizationId" = @organizationId
            )
            SELECT "TargetKind", "TargetPartId", "TargetNonPrintedComponentId", SUM("Multiplier") AS "TotalQuantity"
            FROM explosion
            WHERE "TargetKind" IN ('Part', 'NonPrintedComponent')
            GROUP BY "TargetKind", "TargetPartId", "TargetNonPrintedComponentId";
            """;

        var partTotals = new Dictionary<Guid, decimal>();
        var componentTotals = new Dictionary<Guid, decimal>();

        var connection = dbContext.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandType = CommandType.Text;
            AddParameter(command, "quantity", quantity);
            AddParameter(command, "rootAssemblyId", rootAssemblyId);
            AddParameter(command, "organizationId", organizationId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var targetKind = reader.GetString(0);
                var totalQuantity = reader.GetDecimal(3);

                if (targetKind == nameof(BomLineTargetKind.Part))
                {
                    partTotals[reader.GetGuid(1)] = totalQuantity;
                }
                else
                {
                    componentTotals[reader.GetGuid(2)] = totalQuantity;
                }
            }
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }

        var printedParts = await ResolvePartsAsync(partTotals, cancellationToken);
        var nonPrintedComponents = await ResolveComponentsAsync(componentTotals, cancellationToken);

        return new BomExplosionResult(printedParts, nonPrintedComponents);
    }

    private async Task<IReadOnlyList<ExplodedPartQuantity>> ResolvePartsAsync(Dictionary<Guid, decimal> totals, CancellationToken cancellationToken)
    {
        if (totals.Count == 0)
        {
            return [];
        }

        var ids = totals.Keys.ToList();
        var names = await dbContext.Parts
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

        return totals
            .Select(kvp => new ExplodedPartQuantity(kvp.Key, names.GetValueOrDefault(kvp.Key, string.Empty), kvp.Value))
            .ToList();
    }

    private async Task<IReadOnlyList<ExplodedNonPrintedComponentQuantity>> ResolveComponentsAsync(Dictionary<Guid, decimal> totals, CancellationToken cancellationToken)
    {
        if (totals.Count == 0)
        {
            return [];
        }

        var ids = totals.Keys.ToList();
        var components = await dbContext.NonPrintedComponents
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c, cancellationToken);

        return totals
            .Select(kvp =>
            {
                components.TryGetValue(kvp.Key, out var component);
                return new ExplodedNonPrintedComponentQuantity(kvp.Key, component?.Name ?? string.Empty, kvp.Value);
            })
            .ToList();
    }

    private static void AddParameter(IDbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
