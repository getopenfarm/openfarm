using FluentAssertions;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Domain.Tests.Parts;

public class OptionalPartMetadataTests
{
    [Fact]
    public void UnknownMetadata_CanBeSavedButCannotPrint()
    {
        var part = Create(null, [], requiresSlicing: false);
        part.PrintRequirementsStatus.Should().Be(PrintRequirementsStatus.Incomplete);
        part.Invoking(p => p.EnsurePrintRequirementsComplete()).Should().Throw<PartPrintRequirementsIncompleteException>();
    }

    [Fact]
    public void UnknownSlicedMaterial_IsNotAWildcard()
    {
        var part = Create(0.4m, [new FilamentRequirementSpec(null, null, null, null, null)], requiresSlicing: false);
        part.PrintRequirementsStatus.Should().Be(PrintRequirementsStatus.Incomplete);
    }

    [Fact]
    public void ModelWildcard_CanPrintWithoutEstimates()
    {
        var part = Create(0.4m, [new FilamentRequirementSpec(null, null, null, null, null)], requiresSlicing: true);
        part.Invoking(p => p.EnsurePrintRequirementsComplete()).Should().NotThrow();
        part.EstimateStatus.Should().Be(EstimateStatus.Unavailable);
    }

    [Fact]
    public void Reprocess_PreservesFileRevisionUploadTimeUnitsAndTags()
    {
        var part = Create(null, [], requiresSlicing: true);
        var tag = Tag.Create(part.OrganizationId, "BOM");
        part.AddTag(tag);
        part.SetUnitsPerPrint(3);
        var uploadedAt = part.FileUploadedAtUtc;
        var hash = part.RevisionHash;
        part.Reprocess(0.4m, null, null, EstimateStatus.Partial, 12, null, 1,
            [new FilamentRequirementSpec(null, null, null, null, null)], []);
        part.FileUploadedAtUtc.Should().Be(uploadedAt);
        part.RevisionHash.Should().Be(hash);
        part.RevisionNumber.Should().Be(1);
        part.UnitsPerPrint.Should().Be(3);
        part.Tags.Should().ContainSingle(t => t.Id == tag.Id);
        part.PrintRequirementsStatus.Should().Be(PrintRequirementsStatus.Complete);
    }

    [Theory]
    [InlineData(null, null, EstimateStatus.Unavailable)]
    [InlineData(10, null, EstimateStatus.Partial)]
    [InlineData(null, 5, EstimateStatus.Partial)]
    [InlineData(10, 5, EstimateStatus.Available)]
    public void EstimateAvailability_TreatsFieldsIndependently(int? minutes, int? grams, EstimateStatus expected) =>
        PartEstimateAvailability.From(minutes, grams).Should().Be(expected);

    private static Part Create(decimal? nozzle, IReadOnlyList<FilamentRequirementSpec> requirements, bool requiresSlicing) =>
        Part.Create(Guid.NewGuid(), "Part", requiresSlicing ? PartFileFormat.Stl : PartFileFormat.GCode, requiresSlicing,
            "part.stl", "/tmp/part.stl", 100, nozzle, null, null, EstimateStatus.Unavailable, null, null,
            1, 1, new string('a', 64), requirements, [], []);
}
