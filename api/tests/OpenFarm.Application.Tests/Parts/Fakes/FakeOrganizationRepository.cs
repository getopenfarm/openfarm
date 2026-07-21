using OpenFarm.Application.Organizations;
using OpenFarm.Domain.Organizations;

namespace OpenFarm.Application.Tests.Parts.Fakes;

public sealed class FakeOrganizationRepository(Organization organization) : IOrganizationRepository
{
    public Task<Organization> GetAsync(CancellationToken cancellationToken) => Task.FromResult(organization);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
