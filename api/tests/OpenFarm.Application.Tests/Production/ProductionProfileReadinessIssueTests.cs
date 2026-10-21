using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Production;
using OpenFarm.Application.Profiles;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Tests.Production;

// Whatever goes wrong while checking one job on one printer is a reason that job cannot go there. It never becomes an
// exception that takes the queue page down or stops the dispatcher from looking at the jobs behind it.
public class ProductionProfileReadinessIssueTests
{
    private sealed class FailingPrinterRepository(Exception failure) : IPrinterRepository
    {
        public Task AddAsync(Printer printer, CancellationToken cancellationToken) => throw failure;
        public Task<Printer?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw failure;
        public Task<Printer?> GetBySerialNumberAsync(string serialNumber, CancellationToken cancellationToken) => throw failure;
        public Task<IReadOnlyList<Printer>> ListAsync(CancellationToken cancellationToken) => throw failure;
        public Task RemoveAsync(Printer printer, CancellationToken cancellationToken) => throw failure;
        public Task SaveChangesAsync(CancellationToken cancellationToken) => throw failure;
    }

    private static Task<string?> IssueWhenCheckingFailsWithAsync(Exception failure, CancellationToken ct = default)
    {
        var catalog = new TwoEngineCatalog();
        var printers = new FailingPrinterRepository(failure);
        var compatibility = new ProfileCompatibility(catalog.Machines, catalog.Filaments);
        var readiness = new ProductionProfileReadiness(
            new RepresentativeFilamentResolver(catalog.Filaments, new FakeSpoolRepository(), new FakeFilamentRepository(), new FakeFeedUnitRepository(),
                printers, catalog.Machines, compatibility),
            printers, null!, compatibility, null!, null!, null!, null!, NullLogger<ProductionProfileReadiness>.Instance);
        var part = Part.Create("Part", PartFileFormat.GCode, false, "part.gcode", "/part.gcode",
            10, 0.4m, null, null, EstimateStatus.Available, 10, 10, 1, 1, new string('a', 64),
            [new("PLA", "#000000", null, null, null)], [], []);

        return readiness.GetIssueAsync(Guid.NewGuid(), part, [new JobFilamentRequirementSpec("PLA", "#000000")], ct);
    }

    [Fact]
    public async Task UnexpectedError_IsAReasonTheJobCannotGoThere_NotAnException()
    {
        var issue = await IssueWhenCheckingFailsWithAsync(new InvalidOperationException("boom"));

        issue.Should().Be("Production settings check failed. See the server log.");
    }

    [Fact]
    public async Task KnownConfigurationProblem_IsReportedWithItsOwnMessage()
    {
        var issue = await IssueWhenCheckingFailsWithAsync(new SlicingProfileUnconfiguredException("Select a machine profile."));

        issue.Should().Be("Select a machine profile.");
    }

    [Fact]
    public async Task UnreadableRetainedFile_SaysToReviewOrReplaceIt()
    {
        var issue = await IssueWhenCheckingFailsWithAsync(new IOException("disk"));

        issue.Should().Be("The retained job file is unavailable. Review or replace the part file.");
    }

    [Fact]
    public async Task Cancellation_StaysACancellation()
    {
        var act = () => IssueWhenCheckingFailsWithAsync(new OperationCanceledException());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
