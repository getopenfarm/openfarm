namespace OpenFarm.Domain.Production;

public sealed class InvalidRevisionModeException() : Exception(
    "Finite lines always use Snapshot; unlimited lines always use FollowLatest. Revision behavior cannot be changed.");
