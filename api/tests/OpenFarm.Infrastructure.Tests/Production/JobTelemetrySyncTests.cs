using FluentAssertions;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Production;

namespace OpenFarm.Infrastructure.Tests.Production;

/// <summary>
/// TDD per tasks.md T033's disambiguation-only scope (research.md R12) — the rest of
/// <see cref="JobTelemetrySync"/> is test-after and covered via the (Testcontainers-backed)
/// <c>JobTelemetrySyncTests</c> integration suite, not here.
/// </summary>
public class JobTelemetrySyncTests
{
    [Fact]
    public void ResolveTerminalStatus_WhenCancelRequested_ResolvesCancelledRegardlessOfTerminalGcodeState() =>
        JobTelemetrySync.ResolveTerminalStatus(cancelRequested: true).Should().Be(PrintJobStatus.Cancelled);

    [Fact]
    public void ResolveTerminalStatus_WhenCancelNotRequested_ResolvesFailed() =>
        JobTelemetrySync.ResolveTerminalStatus(cancelRequested: false).Should().Be(PrintJobStatus.Failed);
}
