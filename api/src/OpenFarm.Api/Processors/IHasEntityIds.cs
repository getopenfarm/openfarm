namespace OpenFarm.Api.Processors;

// A response DTO implements this when one request legitimately creates many entities in one call
// (e.g. confirm-import's bulk profile creation, research.md §7) — ActivityLogPostProcessor writes
// one ActivityLogEntry per id when present, instead of its default single-Id-reflection path.
public interface IHasEntityIds
{
    IReadOnlyList<string> EntityIds { get; }
}
