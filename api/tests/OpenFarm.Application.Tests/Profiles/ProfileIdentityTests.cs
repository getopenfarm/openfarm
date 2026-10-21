using FluentAssertions;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Profiles;

public class ProfileIdentityTests
{
    private static FilamentProfile System(string name, string? externalId, string version, string vendor = "Bambu Lab", ProfileSourceFormat format = ProfileSourceFormat.OrcaSlicer) =>
        FilamentProfile.CreateSystemProfile(name, format, null, "{}", vendor, externalId, isSelectable: true,
            compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null, sourceVersion: version);

    [Fact]
    public void Same_SystemPresetInTwoVersions_IsOnePresetEvenWhenRenamed() =>
        ProfileIdentity.Same(System("Old name", "GFSA00", "2.3.1"), System("New name", "GFSA00", "2.4.2")).Should().BeTrue();

    [Fact]
    public void Same_PresetsWithoutSettingId_FallBackToVendorAndName()
    {
        ProfileIdentity.Same(System("PLA @base", null, "2.3.1"), System("PLA @base", null, "2.4.2")).Should().BeTrue();
        ProfileIdentity.Same(System("PLA @base", null, "2.3.1"), System("PETG @base", null, "2.4.2")).Should().BeFalse();
        ProfileIdentity.Same(System("PLA @base", null, "2.3.1", vendor: "Generic"), System("PLA @base", null, "2.4.2")).Should().BeFalse();
    }

    [Fact]
    public void Same_DifferentSettingIdOrEngineFormat_IsADifferentPreset()
    {
        ProfileIdentity.Same(System("A", "GFSA00", "2.4.2"), System("A", "GFSA01", "2.4.2")).Should().BeFalse();
        ProfileIdentity.Same(System("A", "GFSA00", "2.4.2"), System("A", "GFSA00", "2.7.1", format: ProfileSourceFormat.BambuStudio)).Should().BeFalse();
    }

    [Fact]
    public void Same_UserPresets_AreOnlyEverTheSameRow()
    {
        var mine = FilamentProfile.Create("My PLA", ProfileSourceFormat.OrcaSlicer, null, "{}");
        var twin = FilamentProfile.Create("My PLA", ProfileSourceFormat.OrcaSlicer, null, "{}");

        ProfileIdentity.Same(mine, mine).Should().BeTrue();
        ProfileIdentity.Same(mine, twin).Should().BeFalse();
        ProfileIdentity.Same(mine, System("My PLA", "GFSA00", "2.4.2")).Should().BeFalse();
    }
}
