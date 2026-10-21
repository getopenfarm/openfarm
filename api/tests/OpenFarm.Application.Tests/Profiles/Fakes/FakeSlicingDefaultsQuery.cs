using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Tests.Profiles.Fakes;

public sealed class FakeSlicingDefaultsQuery(SlicerEngine? engine = null, string version = "test") : IQueryHandler<GetSlicingDefaultsQuery, SlicingDefaults>
{
    public Task<SlicingDefaults> HandleAsync(GetSlicingDefaultsQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(new SlicingDefaults(engine is { } selected ? new(selected, version, true) : null, []));
}
