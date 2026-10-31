using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed record ProductionPlanPart(Guid PartId, string Name, decimal RequiredUnits, int UnitsPerPlate,
    int Plates, int PlannedUnits, decimal SurplusUnits, decimal? Minutes, decimal? Grams, string Revision,
    DateTimeOffset UpdatedAtUtc, IReadOnlyList<int>? JobUnits = null, string? ObjectMappingVersion = null);
public sealed record ProductionPlan(string Version, IReadOnlyList<ProductionPlanPart> Parts, int Plates,
    decimal? CompleteSets, decimal? Minutes, decimal? Grams);
public sealed record ProductionPlanInput(RunLineTargetKind TargetKind, Guid TargetId, int Quantity,
    QuantityBasis QuantityBasis, IReadOnlyDictionary<Guid, int>? PlateOverrides = null);
public sealed class ProductionPlanConflictException() : Exception("Production inputs changed. Review the updated plan before sending.");
public sealed class InvalidProductionDemandException(string message) : Exception(message);

/// <summary>Shared exact-quantity planner for previews, direct demand and tracked production.</summary>
public sealed class ProductionPlanner(IPartRepository parts, IAssemblyGraphQueryService graph, IPrintObjectManifestReader? objects = null, IPartFileStorage? fileStorage = null)
{
    public async Task<PrintObjectManifest?> ReadObjectsAsync(OpenFarm.Domain.Parts.Part part, CancellationToken ct)
    {
        if (objects is null) return null;
        try
        {
            var manifest = await objects.ReadAsync(part, ct);
            return manifest.Units.Count == part.UnitsPerPrint ? manifest : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Xml.XmlException) { return null; }
    }

    public async Task ValidateQuantityAsync(OpenFarm.Domain.Parts.Part part, CancellationToken ct)
    {
        if (fileStorage is null) return;
        var inventory = await PartObjectInventory.ReadAsync(fileStorage, part.StoragePath, part.PlateNumber, ct);
        if (inventory.Count is { } detected && detected != part.UnitsPerPrint)
            throw new InvalidProductionDemandException($"{part.Name}: detected {detected} objects but the saved quantity is {part.UnitsPerPrint}. Reprocess this Part before queueing.");
    }

    public async Task<ProductionPlan> PlanAsync(ProductionPlanInput input, CancellationToken ct)
    {
        if (input.Quantity < 1 || !Enum.IsDefined(input.QuantityBasis)
            || (input.TargetKind == RunLineTargetKind.Assembly ? input.QuantityBasis != QuantityBasis.Sets : input.QuantityBasis == QuantityBasis.Sets))
            throw new InvalidProductionDemandException("Choose a positive quantity and a quantity basis matching the target.");
        if (input.PlateOverrides?.Count > 0)
            throw new InvalidProductionDemandException("Component plate overrides are deprecated. Assembly quantities follow the requested set count.");
        var needs = input.TargetKind == RunLineTargetKind.Assembly
            ? (await graph.ExplodeAsync(input.TargetId, input.Quantity, ct)).PrintedParts
            : [new ExplodedPartQuantity(input.TargetId, "", input.Quantity)];
        if (needs.Count == 0) throw new RunLineHasNoPrintedPartsException(input.TargetId);
        var result = new List<ProductionPlanPart>();
        foreach (var need in needs.OrderBy(n => n.PartId))
        {
            var part = await parts.GetByIdAsync(need.PartId, ct) ?? throw new OpenFarm.Domain.Parts.PartNotFoundException(need.PartId);
            part.EnsurePrintRequirementsComplete();
            await ValidateQuantityAsync(part, ct);
            var units = input.QuantityBasis == QuantityBasis.Plates ? (decimal)input.Quantity * part.UnitsPerPrint : need.Quantity;
            if (units != decimal.Truncate(units))
                throw new InvalidProductionDemandException("Printed component requirements must be whole units.");
            if (units < 1 || units > int.MaxValue) throw new InvalidProductionDemandException("Required output exceeds the supported quantity range.");
            var plates = checked((int)Math.Ceiling(units / part.UnitsPerPrint));
            var planned = checked((int)units);
            var jobUnits = Enumerable.Range(0, plates).Select(i => Math.Min(part.UnitsPerPrint, planned - i * part.UnitsPerPrint)).ToArray();
            string? mappingVersion = null;
            if (planned % part.UnitsPerPrint != 0 && objects is not null)
            {
                try
                {
                    var manifest = await objects.ReadAsync(part, ct);
                    manifest.Validate(part.UnitsPerPrint);
                    mappingVersion = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest))));
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Xml.XmlException) { throw new InvalidProductionDemandException($"{part.Name}: {ex.Message}"); }
            }
            result.Add(new(part.Id, part.Name, units, part.UnitsPerPrint, plates, planned, planned - units,
                planned % part.UnitsPerPrint == 0 ? part.EstimatedPrintTimeMinutes * plates : null,
                planned % part.UnitsPerPrint == 0 ? part.EstimatedFilamentWeightGrams * plates : null,
                part.RevisionHash, part.UpdatedAtUtc, jobUnits, mappingVersion));
        }
        // Include canonical inputs and every relevant Part/BOM value: a stale preview must never silently queue a different plate plan.
        var fingerprint = JsonSerializer.Serialize(new { input.TargetKind, input.TargetId, input.Quantity, input.QuantityBasis, Parts = result });
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)));
        return new(version, result, result.Sum(p => p.Plates), input.TargetKind == RunLineTargetKind.Assembly
            ? Math.Floor(result.Min(p => p.PlannedUnits / (p.RequiredUnits / input.Quantity))) : null,
            result.All(p => p.Minutes is not null) ? result.Sum(p => p.Minutes) : null,
            result.All(p => p.Grams is not null) ? result.Sum(p => p.Grams) : null);
    }
}
