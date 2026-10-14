using OpenFarm.Application.Parts;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Assemblies;

/// <summary>Shared target-existence check and spec mapping for every handler that adds BOM lines.</summary>
internal static class BomLineTargets
{
    public static async Task EnsureExistsAsync(
        BomLineTargetKind targetKind,
        Guid targetId,
        IPartRepository partRepository,
        IAssemblyRepository assemblyRepository,
        INonPrintedComponentRepository nonPrintedComponentRepository,
        CancellationToken cancellationToken)
    {
        switch (targetKind)
        {
            case BomLineTargetKind.Part:
                if (await partRepository.GetByIdAsync(targetId, cancellationToken) is null)
                {
                    throw new PartNotFoundException(targetId);
                }

                break;
            case BomLineTargetKind.Assembly:
                if (!await assemblyRepository.ExistsAsync(targetId, cancellationToken))
                {
                    throw new AssemblyNotFoundException(targetId);
                }

                break;
            case BomLineTargetKind.NonPrintedComponent:
                if (!await nonPrintedComponentRepository.ExistsAsync(targetId, cancellationToken))
                {
                    throw new NonPrintedComponentNotFoundException(targetId);
                }

                break;
        }
    }

    public static BomLineSpec ToSpec(BomLineTargetKind targetKind, Guid targetId, decimal quantity) => targetKind switch
    {
        BomLineTargetKind.Part => new BomLineSpec(BomLineTargetKind.Part, targetId, null, null, quantity),
        BomLineTargetKind.Assembly => new BomLineSpec(BomLineTargetKind.Assembly, null, targetId, null, quantity),
        BomLineTargetKind.NonPrintedComponent => new BomLineSpec(BomLineTargetKind.NonPrintedComponent, null, null, targetId, quantity),
        _ => throw new ArgumentOutOfRangeException(nameof(targetKind)),
    };
}
