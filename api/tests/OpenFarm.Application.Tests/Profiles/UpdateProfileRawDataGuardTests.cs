using FluentAssertions;
using OpenFarm.Application.Profiles;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Profiles;

// F7 — the raw-data editors must refuse System rows and anything that is not a JSON object *before* the
// row is touched: ProfileRawDataMerger enumerates object properties, so a saved `[]` used to make every
// later read of that row throw.
public class UpdateProfileRawDataGuardTests
{
    private const string OriginalJson = """{"name":"Original"}""";

    [Theory]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("42")]
    [InlineData("null")]
    public async Task Machine_NonObjectJson_IsRejectedAndRowUnchanged(string rawDataJson)
    {
        var repository = new FakeMachineProfileRepository();
        var row = MachineProfile.Create("Printer", ProfileSourceFormat.OrcaSlicer, null, OriginalJson);
        await repository.AddAsync(row, CancellationToken.None);
        var handler = new UpdateMachineProfileRawDataCommandHandler(repository, new FakeProfileBundleParser());

        var act = () => handler.HandleAsync(new UpdateMachineProfileRawDataCommand(row.Id, rawDataJson), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidProfileJsonException>().WithMessage("*must be a JSON object*");
        row.RawDataJson.Should().Be(OriginalJson);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("42")]
    [InlineData("null")]
    public async Task Filament_NonObjectJson_IsRejectedAndRowUnchanged(string rawDataJson)
    {
        var repository = new FakeFilamentProfileRepository();
        var row = FilamentProfile.Create("PLA", ProfileSourceFormat.OrcaSlicer, null, OriginalJson);
        await repository.AddAsync(row, CancellationToken.None);
        var handler = new UpdateFilamentProfileRawDataCommandHandler(repository, new FakeProfileBundleParser());

        var act = () => handler.HandleAsync(new UpdateFilamentProfileRawDataCommand(row.Id, rawDataJson), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidProfileJsonException>().WithMessage("*must be a JSON object*");
        row.RawDataJson.Should().Be(OriginalJson);
    }

    [Fact]
    public async Task Machine_MalformedJson_KeepsTheSyntaxMessage()
    {
        var repository = new FakeMachineProfileRepository();
        var row = MachineProfile.Create("Printer", ProfileSourceFormat.OrcaSlicer, null, OriginalJson);
        await repository.AddAsync(row, CancellationToken.None);
        var handler = new UpdateMachineProfileRawDataCommandHandler(repository, new FakeProfileBundleParser());

        var act = () => handler.HandleAsync(new UpdateMachineProfileRawDataCommand(row.Id, "{not valid json"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidProfileJsonException>().WithMessage("Machine profile raw data is not valid JSON*");
    }

    [Fact]
    public async Task Machine_SystemRow_IsReadOnly()
    {
        var repository = new FakeMachineProfileRepository();
        var row = MachineProfile.CreateSystemProfile("Bambu Lab P1S 0.4 nozzle", ProfileSourceFormat.OrcaSlicer, null, OriginalJson,
            "Bambulab", "GM014", isSelectable: true, vendorMachineModelName: "Bambu Lab P1S", includedTemplateNames: null, sourceVersion: "1.0.0");
        await repository.AddAsync(row, CancellationToken.None);
        var handler = new UpdateMachineProfileRawDataCommandHandler(repository, new FakeProfileBundleParser());

        var act = () => handler.HandleAsync(new UpdateMachineProfileRawDataCommand(row.Id, """{"name":"Edited"}"""), CancellationToken.None);

        await act.Should().ThrowAsync<SystemProfileReadOnlyException>();
        row.RawDataJson.Should().Be(OriginalJson);
    }

    [Fact]
    public async Task Filament_SystemRow_IsReadOnly()
    {
        var repository = new FakeFilamentProfileRepository();
        var row = FilamentProfile.CreateSystemProfile("Bambu PLA Basic @BBL X1C", ProfileSourceFormat.OrcaSlicer, null, OriginalJson,
            "Bambu Lab", "GFSA00", isSelectable: true, compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null, sourceVersion: "1.0.0");
        await repository.AddAsync(row, CancellationToken.None);
        var handler = new UpdateFilamentProfileRawDataCommandHandler(repository, new FakeProfileBundleParser());

        var act = () => handler.HandleAsync(new UpdateFilamentProfileRawDataCommand(row.Id, """{"name":"Edited"}"""), CancellationToken.None);

        await act.Should().ThrowAsync<SystemProfileReadOnlyException>();
        row.RawDataJson.Should().Be(OriginalJson);
    }
}
