using OpenFarm.Domain.Production;
namespace OpenFarm.Application.Production;

internal static class ProductionPlanValidation
{
    public static async Task ValidateAsync(IReadOnlyList<RunLineInput> lines, ProductionPlanner? planner,
        IProductionDemandRepository? demands, CancellationToken ct)
    {
        if (planner is null) return;
        foreach (var line in lines.Where(l => l.Quantity is not null))
        {
            if (line.TargetKind == RunLineTargetKind.Assembly && line.QuantityBasis == QuantityBasis.Plates)
                throw new InvalidProductionDemandException("Run assemblies use set targets; component plate counts are standalone demand.");
            var plan = await planner.PlanAsync(new(line.TargetKind, line.TargetId, line.Quantity!.Value,
                line.QuantityBasis ?? (line.TargetKind == RunLineTargetKind.Part ? QuantityBasis.Units : QuantityBasis.Sets)), ct);
            if (line.PlanVersion is not null && plan.Version != line.PlanVersion) throw new ProductionPlanConflictException();
            if (demands is not null) await demands.ValidateSourcesAsync(plan, ct);
        }
    }
}
