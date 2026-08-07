namespace OpenFarm.Application.Production;

/// <summary>
/// Application-facing seam over <c>ProductionDispatchBackgroundService</c>'s own queue — mirrors
/// <c>IPartProcessingDispatcher</c>'s relationship to <c>PartProcessingBackgroundService</c>
/// (research.md R4).
/// </summary>
public interface IProductionDispatcher
{
    void RequestDispatchAttempt(Guid printerId);
}
