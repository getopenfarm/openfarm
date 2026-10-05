namespace OpenFarm.Domain.Parts;

/// <summary>Estimation can be active, complete, partial, or unavailable independently of catalog readiness.</summary>
public enum EstimateStatus
{
    Resolving,
    Available,
    Unavailable,
    Partial,
}
