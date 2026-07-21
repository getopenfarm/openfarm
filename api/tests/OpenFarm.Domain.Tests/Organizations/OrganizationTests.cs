using FluentAssertions;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Domain.Tests.Organizations;

public class OrganizationTests
{
    [Fact]
    public void Create_NewOrganization_HasNoExplicitDefaultSlicer()
    {
        var organization = Organization.Create("Acme Print Farm");

        organization.DefaultSlicerVersion.Should().BeNull();
        organization.DefaultSlicer.Should().BeNull();
    }

    [Fact]
    public void SetDefaultSlicer_AcceptsASlicerRef_PersistsBothHalves()
    {
        var organization = Organization.Create("Acme Print Farm");

        organization.SetDefaultSlicer(new SlicerRef(SlicerEngine.SuperSlicer, "2.5.59.13"));

        organization.DefaultSlicerEngine.Should().Be(SlicerEngine.SuperSlicer);
        organization.DefaultSlicerVersion.Should().Be("2.5.59.13");
        organization.DefaultSlicer.Should().Be(new SlicerRef(SlicerEngine.SuperSlicer, "2.5.59.13"));
    }

    [Fact]
    public void SetDefaultSlicer_CalledAgain_ReplacesThePreviousDefault()
    {
        var organization = Organization.Create("Acme Print Farm");
        organization.SetDefaultSlicer(new SlicerRef(SlicerEngine.OrcaSlicer, "2.2.0"));

        organization.SetDefaultSlicer(new SlicerRef(SlicerEngine.BambuStudio, "01.10.00.00"));

        organization.DefaultSlicer.Should().Be(new SlicerRef(SlicerEngine.BambuStudio, "01.10.00.00"));
    }
}
