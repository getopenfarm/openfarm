using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Tests.Parts.Fakes;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.Organizations;

namespace OpenFarm.Application.Tests.Parts;

public class GetSlicingProfileDefaultsQueryHandlerTests
{
    [Fact]
    public async Task HandleAsync_NothingConfigured_ReturnsNullDefaultMachineProfileAndEmptyFilamentDefaults()
    {
        var organization = Organization.Create("Test Org");
        var handler = new GetSlicingProfileDefaultsQueryHandler(
            new FakeOrganizationRepository(organization),
            new FakeMachineProfileRepository(),
            new FakeProcessProfileRepository(),
            new FakeFilamentProfileRepository(),
            new FakeOrganizationFilamentDefaultRepository());

        var result = await handler.HandleAsync(new GetSlicingProfileDefaultsQuery(), CancellationToken.None);

        result.DefaultMachineProfile.Should().BeNull();
        result.FilamentDefaults.Should().BeEmpty();
    }
}
