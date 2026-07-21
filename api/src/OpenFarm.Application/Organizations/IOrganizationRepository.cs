using OpenFarm.Domain.Organizations;

namespace OpenFarm.Application.Organizations;

/// <summary>Self-hosted installs get exactly one auto-created Organization (CLAUDE.md) — always exists once FirstBootSeeder has run.</summary>
public interface IOrganizationRepository
{
    Task<Organization> GetAsync(CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
