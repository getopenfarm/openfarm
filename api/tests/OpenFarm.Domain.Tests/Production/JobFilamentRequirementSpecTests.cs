using FluentAssertions;
using OpenFarm.Domain.Production;

namespace OpenFarm.Domain.Tests.Production;

public class JobFilamentRequirementSpecTests
{
    private static readonly Guid Filament = Guid.NewGuid();
    private static readonly Guid White = Guid.NewGuid();
    private static readonly Guid Black = Guid.NewGuid();

    [Fact]
    public void FileColour_AcceptsAnyBrandWithTheSameMaterialAndHex()
    {
        var spec = new JobFilamentRequirementSpec("PLA", "#ffffff");

        spec.Accepts(Guid.NewGuid(), Guid.NewGuid(), "PLA", "#FFFFFF").Should().BeTrue();
        spec.Accepts(White, Filament, "PLA", "#000000").Should().BeFalse();
        spec.Accepts(White, Filament, "PETG", "#FFFFFF").Should().BeFalse();
        spec.MatchingValues.Should().Be(("PLA", "#ffffff"));
    }

    [Fact]
    public void ChosenColour_AcceptsOnlyThatVariant_WhateverTheFileColour()
    {
        var spec = new JobFilamentRequirementSpec("PLA", "#ffffff", NarrowFilamentVariantId: Black);

        spec.Accepts(Black, Filament, "PLA", "#000000").Should().BeTrue();
        spec.Accepts(White, Filament, "PLA", "#FFFFFF").Should().BeFalse();
        spec.MatchingValues.Should().Be(("PLA", (string?)null));
    }

    [Fact]
    public void AnyColourOfAFilament_AcceptsEveryColourOfIt_AndNoOtherFilament()
    {
        var spec = new JobFilamentRequirementSpec("PLA", "#ffffff", NarrowFilamentId: Filament);

        spec.Accepts(White, Filament, "PLA", "#FFFFFF").Should().BeTrue();
        spec.Accepts(Black, Filament, "PLA", "#000000").Should().BeTrue();
        spec.Accepts(White, Guid.NewGuid(), "PLA", "#FFFFFF").Should().BeFalse();
        spec.MatchingValues.Should().Be(("PLA", (string?)null));
    }

    [Fact]
    public void AnyColorOfTheMaterial_AcceptsEveryBrandAndColour_ButNotAnotherMaterial()
    {
        var spec = new JobFilamentRequirementSpec("PLA", "#ffffff", AnyColor: true);

        spec.Accepts(Black, Guid.NewGuid(), "PLA", "#000000").Should().BeTrue();
        spec.Accepts(White, Filament, "PETG", "#FFFFFF").Should().BeFalse();
        spec.MatchingValues.Should().Be(("PLA", (string?)null));
    }

    [Fact]
    public void AnyMaterial_AcceptsEverything()
    {
        new JobFilamentRequirementSpec(null, null).Accepts(White, Filament, "TPU", "#123456").Should().BeTrue();
    }
}
