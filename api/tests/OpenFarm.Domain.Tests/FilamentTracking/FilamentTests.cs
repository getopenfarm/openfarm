using FluentAssertions;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Domain.Tests.FilamentTracking;

public class FilamentTests
{
    [Fact]
    public void UpdateDetails_ReplacesVendorMaterialAndType()
    {
        var filament = Filament.Create(Guid.NewGuid(), Guid.NewGuid(), "PLA Basic", "PLA");
        var vendorId = Guid.NewGuid();

        filament.UpdateDetails(vendorId, "PETG HF", "PETG");

        filament.VendorId.Should().Be(vendorId);
        filament.Material.Should().Be("PETG HF");
        filament.MaterialType.Should().Be("PETG");
    }

    [Theory]
    [InlineData(" ", "PLA")]
    [InlineData("PLA Basic", "")]
    public void UpdateDetails_RejectsBlankMaterialOrType(string material, string materialType)
    {
        var filament = Filament.Create(Guid.NewGuid(), Guid.NewGuid(), "PLA Basic", "PLA");

        var act = () => filament.UpdateDetails(Guid.NewGuid(), material, materialType);

        act.Should().Throw<ArgumentException>();
        filament.Material.Should().Be("PLA Basic");
    }
}
