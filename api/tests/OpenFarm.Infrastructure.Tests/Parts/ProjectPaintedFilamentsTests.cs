using System.IO.Compression;
using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Parts;

namespace OpenFarm.Infrastructure.Tests.Parts;

public class ProjectPaintedFilamentsTests
{
    private const string Project = """{"printer_model":"Snapmaker U1","printer_settings_id":"Snapmaker U1 (0.4 nozzle)","print_settings_id":"Custom process","curr_bed_type":"Textured PEI Plate","nozzle_diameter":["0.4"],"filament_type":["PLA","PLA","PLA","PLA"],"filament_colour":["#FFCC3C","#FFFFFF","#000000","#CE0000"],"filament_vendor":["Aliz","Aliz","Aliz","Aliz"]}""";
    private const string Settings = """
        <config>
          <object id="2"><metadata key="extruder" value="3"/></object>
          <object id="4"><metadata key="extruder" value="2"/></object>
          <plate><metadata key="plater_id" value="1"/><metadata key="plater_name" value="Pikachu"/><model_instance><metadata key="object_id" value="2"/></model_instance></plate>
          <plate><metadata key="plater_id" value="2"/><metadata key="plater_name" value="General"/><model_instance><metadata key="object_id" value="4"/></model_instance></plate>
        </config>
        """;

    private static string Model(string objects) => $"""
        <model xmlns="http://schemas.microsoft.com/3dmanufacturing/core/2015/02" xmlns:p="http://schemas.microsoft.com/3dmanufacturing/production/2015/06"><resources>{objects}</resources></model>
        """;
    private static string Mesh(string id, string paint, string attribute = "paint_color") =>
        $"<object id=\"{id}\"><mesh><triangles><triangle {attribute}=\"{paint}\"/></triangles></mesh></object>";
    private static string Component(string id, string target, string? path = null) =>
        $"<object id=\"{id}\"><components><component objectid=\"{target}\"{(path is null ? "" : $" p:path=\"{path}\"")}/></components></object>";

    private static MemoryStream Archive(string root, params (string Path, string Xml)[] models)
    {
        var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var (path, xml) in models.Prepend(("3D/3dmodel.model", root))
                         .Append(("Metadata/model_settings.config", Settings)).Append(("Metadata/project_settings.config", Project)))
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                writer.Write(xml);
            }
        }
        output.Position = 0;
        return output;
    }

    [Theory]
    [InlineData("paint_color")]
    [InlineData("mmu_segmentation")]
    public async Task SeparatePaintedPlates_KeepTheirOwnColoursAndCompleteWeights(string attribute)
    {
        using var file = Archive(Model(Component("2", "1", "/3D/Objects/gold.model") + Component("4", "1", "/3D/Objects/red.model")),
            ("3D/Objects/gold.model", Model(Mesh("1", "4", attribute))),
            ("3D/Objects/red.model", Model(Mesh("1", "0C1C1", attribute))));
        var resolved = await new UnslicedProjectMaterialResolver().ResolveAsync(file, CancellationToken.None);
        resolved.Plates[0].FilamentSlots.Select(s => s.DetectedColorHex).Should().Equal("#FFCC3C", "#000000");
        resolved.Plates[1].FilamentSlots.Select(s => s.DetectedColorHex).Should().Equal("#FFFFFF", "#000000", "#CE0000");

        var requirements = FilamentRequirementGrouper.Group(resolved.FilamentSlots!);
        var plates = FilamentRequirementGrouper.Plates(requirements, resolved.Plates);
        var job = PartProcessingJob.CreateAdd(PartFileFormat.ThreeMf, "labels.3mf", "labels.3mf", 10, true,
            SlicerEngine.OrcaSlicer, "2.5.1", null, null, null);
        job.MarkReadyForReview(0.4m, requirements, plates, EstimateStatus.Resolving, null, null, []);
        IReadOnlyList<FilamentGrams> pikachu = [new("PLA", "#FFCC3C", 0.68m), new("PLA", "#000000", 3.8384m)];
        IReadOnlyList<FilamentGrams> general = [new("PLA", "#FFFFFF", 6.3281m), new("PLA", "#000000", 0.47m), new("PLA", "#CE0000", 0.87m)];
        job.ResolveEstimate(43, 12.08m, [], pikachu.Concat(general).ToList(),
            [plates[0] with { FilamentGrams = pikachu }, plates[1] with { FilamentGrams = general }]);
        var estimates = job.PreviewPlates.Select(p => PrintEstimates.Calculate(1, 1, 10, 100,
            job.PlateRequirements(p).Select(r => new EstimateColor(r.MaterialType, r.DetectedColorHex, null, r.EstimatedGrams)).ToList()));
        var total = PrintEstimates.Sum(estimates);
        total.Colors.Should().HaveCount(4);
        total.Colors.Should().OnlyContain(c => c.ColorHex != null && c.Grams != null && c.Missing == 0);
        total.Colors.Single(c => c.ColorHex == "#FFCC3C").Grams.Should().Be(0.68m);
        total.Colors.Single(c => c.ColorHex == "#CE0000").Grams.Should().Be(0.87m);
        total.Filament.Value.Should().Be(12.1865m);

        file.Position = 0;
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        ProjectUsedFilaments.Read(zip, 1)!.Order().Should().Equal(0, 2);
        ProjectUsedFilaments.Read(zip, 2)!.Order().Should().Equal(1, 2, 3);
    }

    [Fact]
    public void SameModelObjects_AndNestedSharedExternalComponents_AreScopedByObjectIdAndPath()
    {
        using var file = Archive(Model(Component("2", "10") + Component("10", "1", "Objects/shared%20paint.model")
            + Component("4", "20", "/3D/Objects/other.model") + Mesh("99", "1C")),
            ("3D/Objects/shared paint.model", Model(Component("1", "7", "../paint.model"))),
            ("3D/paint.model", Model(Mesh("7", "4") + Mesh("8", "zz"))),
            ("3D/Objects/other.model", Model(Component("20", "7", "../paint.model"))));
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        ProjectUsedFilaments.Read(zip, 1)!.Order().Should().Equal(0, 2);
        ProjectUsedFilaments.Read(zip, 2)!.Order().Should().Equal(0, 1);
        ProjectUsedFilaments.Read(zip, null)!.Order().Should().Equal(0, 1, 2);
    }

    [Theory]
    [InlineData("<object id=\"2\"><components><component objectid=\"2\"/></components></object>")]
    [InlineData("<object id=\"2\"><components><component objectid=\"999\"/></components></object>")]
    [InlineData("<object id=\"2\"><components><component objectid=\"1\" p:path=\"/3D/missing.model\"/></components></object>")]
    [InlineData("<object id=\"2\"><mesh><triangle paint_color=\"zz\"/></mesh></object>")]
    [InlineData("<object id=\"2\"><mesh>")]
    public async Task UnresolvedSelectedGeometry_KeepsAllConfiguredSlots(string geometry)
    {
        using var file = Archive(Model(geometry + Mesh("4", "4")));
        var resolved = await new UnslicedProjectMaterialResolver().ResolveAsync(file, CancellationToken.None);
        resolved.Plates[0].FilamentSlots.Should().HaveCount(4);
        file.Position = 0;
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        ProjectUsedFilaments.Read(zip, 1).Should().BeNull();
    }

    [Fact]
    public void InlineMeshes_AndUnpaintedObjectAssignments_KeepTheirOriginalSlots()
    {
        using var file = Archive(Model(Mesh("2", "4") + "<object id=\"4\"><mesh><triangle/></mesh></object>"));
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        ProjectUsedFilaments.Read(zip, 1)!.Order().Should().Equal(0, 2);
        ProjectUsedFilaments.Read(zip, 2)!.Order().Should().Equal(1);
    }

    [Theory]
    [InlineData("clip_orca.3mf")]
    [InlineData("clip_bambu_studio.3mf")]
    public async Task ExistingSinglePlateSlicerFixtures_KeepTheirFilamentRequirements(string name)
    {
        using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Parts", "Fixtures", "P1SMultiUnit", name));
        var result = await new UnslicedProjectMaterialResolver().ResolveAsync(file, CancellationToken.None);
        result.Plates.Should().ContainSingle().Which.FilamentSlots.Should().ContainSingle().Which.DetectedColorHex.Should().Be("#FFFFFF");
    }
}
