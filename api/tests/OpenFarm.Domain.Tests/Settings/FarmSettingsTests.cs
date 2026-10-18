using FluentAssertions;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Settings;

namespace OpenFarm.Domain.Tests.Settings;

public class FarmSettingsTests
{
    [Fact]
    public void Create_NewSettings_HasNoExplicitDefaultSlicer()
    {
        var settings = FarmSettings.Create();

        settings.DefaultSlicerVersion.Should().BeNull();
        settings.DefaultSlicer.Should().BeNull();
    }

    [Fact]
    public void SetDefaultSlicer_AcceptsASlicerRef_PersistsBothHalves()
    {
        var settings = FarmSettings.Create();

        settings.SetDefaultSlicer(new SlicerRef(SlicerEngine.SuperSlicer, "2.5.59.13"));

        settings.DefaultSlicerEngine.Should().Be(SlicerEngine.SuperSlicer);
        settings.DefaultSlicerVersion.Should().Be("2.5.59.13");
        settings.DefaultSlicer.Should().Be(new SlicerRef(SlicerEngine.SuperSlicer, "2.5.59.13"));
    }

    [Fact]
    public void SetDefaultSlicer_CalledAgain_ReplacesThePreviousDefault()
    {
        var settings = FarmSettings.Create();
        settings.SetDefaultSlicer(new SlicerRef(SlicerEngine.OrcaSlicer, "2.2.0"));

        settings.SetDefaultSlicer(new SlicerRef(SlicerEngine.BambuStudio, "01.10.00.00"));

        settings.DefaultSlicer.Should().Be(new SlicerRef(SlicerEngine.BambuStudio, "01.10.00.00"));
    }
}
