namespace OpenFarm.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF-mapping-only shape for the <c>PartTags</c> join table — a real CLR type rather than a
/// <c>Dictionary&lt;string, object&gt;</c> shared-type entity, since the latter is untested in this
/// codebase and this is the more conventional, better-supported EF Core many-to-many pattern. Not a
/// Domain concept — pure relational plumbing, never referenced outside <see cref="PartConfiguration"/>.
/// </summary>
internal sealed class PartTagJoinRow
{
    public Guid PartId { get; set; }

    public Guid TagId { get; set; }
}
