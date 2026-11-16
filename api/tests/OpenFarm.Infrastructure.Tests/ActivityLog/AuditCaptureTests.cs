using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.AuditLog;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.Printers;
using OpenFarm.Infrastructure.ActivityLog;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Tests.ActivityLog;

public class AuditCaptureTests
{
    private static OpenFarmDbContext Context() => new(new DbContextOptionsBuilder<OpenFarmDbContext>()
        .UseNpgsql("Host=localhost;Database=unused").Options);

    [Fact]
    public void CapturesNamesDiffsAndChannel_WithoutPersistingTelemetry()
    {
        using var db = Context();
        var printer = Printer.Create("Original", "Bambu", "P1S", "10.0.0.1", "audit-test");
        db.Printers.Attach(printer);
        db.Entry(printer).Property(p => p.Name).CurrentValue = "Renamed";
        printer.ObserveReportedState("PAUSE", DateTimeOffset.UtcNow);
        var actor = new AuditContext(Guid.NewGuid(), "Owner", new("Token", Guid.NewGuid(), "Automation"), "rename", "request-1", "Printer", "PrinterEdited");
        var captured = AuditCapture.Prepare(db, actor, "fallback");
        var entry = captured.Should().ContainSingle().Subject;
        entry.EntityLabel.Should().Be("Renamed");
        entry.ActorName.Should().Be("Owner");
        entry.CredentialLabel.Should().Be("Automation");
        entry.CorrelationId.Should().Be("request-1");
        entry.ChangesJson.Should().Contain("Original").And.Contain("Renamed").And.NotContain("PAUSE");
    }

    [Fact]
    public void RepeatedTelemetryAndUnchangedSaves_DoNotCreateAuditEntries()
    {
        using var db = Context();
        var printer = Printer.Create("Printer", "Bambu", "P1S", "10.0.0.1", "audit-test");
        db.Printers.Attach(printer);
        printer.ObserveReportedState("PAUSE", DateTimeOffset.UtcNow);
        AuditCapture.Prepare(db, null, "background").Should().BeEmpty();
    }

    [Fact]
    public void RedactsNestedSecretsAndJsonStrings()
    {
        var value = AuditCapture.SafeValue("RawDataJson", """{"name":"Profile","nested":{"api_key":"secret","accessCode":"code"},"items":[{"password":"pw"}]}""");
        var json = JsonSerializer.Serialize(value);
        json.Should().Contain("Profile").And.Contain("[redacted]").And.NotContain("secret").And.NotContain("code\"").And.NotContain("pw");
        AuditCapture.SafeValue("EncryptedAccessCode", "ciphertext").Should().Be("[redacted]");
    }

    [Fact]
    public void ExplicitChanges_AreRedactedAtTheEntryBoundary()
    {
        var entry = ActivityLogEntry.Create("User", Guid.NewGuid().ToString(), "PasswordChanged", null, ActorType.System);
        entry.RecordChanges("action", "User", [new("PasswordHash", "old-hash", "new-hash")]);
        entry.ChangesJson.Should().Contain("[redacted]").And.NotContain("old-hash").And.NotContain("new-hash");
    }

    [Fact]
    public void PermissionNamesContainingToken_RemainVisibleInChanges()
    {
        var entry = ActivityLogEntry.Create("Group", Guid.NewGuid().ToString(), "GroupUpdated", null, ActorType.System);
        entry.RecordChanges("update", "Operators", [new("Permissions.access-tokens:manage.ClaimValue", null, "access-tokens:manage")]);
        entry.ChangesJson.Should().Contain("access-tokens:manage").And.NotContain("[redacted]");
    }

    [Fact]
    public void MachineObservationsRemainSeparate_AndStoredEntriesCannotBeChanged()
    {
        using var db = Context();
        var entry = ActivityLogEntry.Create("Printer", Guid.NewGuid().ToString(), "PrinterStatusObserved", null, ActorType.System);
        entry.Category.Should().Be("MachineObservation");
        db.Attach(entry);
        db.Entry(entry).Property(e => e.EventType).CurrentValue = "Altered";
        var capture = () => AuditCapture.Prepare(db, null, "request");
        capture.Should().Throw<InvalidOperationException>().WithMessage("*append-only*");
    }

    [Fact]
    public void SemanticEntryAndFieldCapture_ProduceOneEntry()
    {
        using var db = Context();
        var printer = Printer.Create("Printer", "Bambu", "P1S", "10.0.0.1", "audit-test");
        db.Printers.Add(printer);
        var semantic = ActivityLogEntry.Create("Printer", printer.Id.ToString(), "PrinterRegistered", null, ActorType.System);
        db.ActivityLogEntries.Add(semantic);
        AuditCapture.Prepare(db, null, "request").Should().BeEmpty();
        db.ChangeTracker.Entries<ActivityLogEntry>().Should().ContainSingle();
        semantic.ChangesJson.Should().Contain("Printer");
        semantic.EntityLabel.Should().Be("Printer");
    }
}
