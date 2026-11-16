using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using OpenFarm.Application.AuditLog;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.ActivityLog;

/// <summary>Only named business fields enter the log. New persistence fields are excluded by default.</summary>
internal static class AuditCapture
{
    private static readonly string[] LabelFields = ["Name", "Label", "DisplayName", "UserName", "OriginalFileName", "Material", "ColorName", "TaskName", "Device"];
    private static readonly Dictionary<string, string[]> Fields = new(StringComparer.Ordinal)
    {
        ["Printer"] = Split("Name Brand ModelCode IpAddress SerialNumber BuildPlateType MachineProfileId Status"),
        ["Part"] = Split("Name FolderId OriginalFileName FileFormat RequiresSlicing NozzleDiameterMm UnitsPerPrint RevisionHash RevisionNumber ObjectManifestJson PricingMode PricingValue EstimateStatus EstimatedPrintTimeMinutes EstimatedFilamentWeightGrams"),
        ["Assembly"] = Split("Name Description FolderId PricingMode PricingValue"),
        ["LibraryFolder"] = Split("Name ParentId Library"),
        ["BomLine"] = Split("TargetKind TargetPartId TargetAssemblyId TargetNonPrintedComponentId Quantity"),
        ["PartTagJoinRow"] = Split("TagId"),
        ["Tag"] = Split("Name"),
        ["PartFilamentRequirement"] = Split("MaterialType DetectedColorHex DetectedSubBrand NarrowFilamentVariantId NarrowFilamentProfileId NarrowFilamentId AnyColor EstimatedGrams"),
        ["NonPrintedComponent"] = Split("Name UnitCost ItemType Category Unit PricingMode PackageLabel PackagePrice PackageQuantity Length LengthUnit Width WidthUnit Icon"),
        ["Vendor"] = Split("Name"),
        ["Filament"] = Split("VendorId Material MaterialType PricePerKg LowStockGrams LowStockDays"),
        ["FilamentProfileAssignment"] = Split("Brand ModelCode NozzleDiameterMm FilamentProfileId FilamentVariantId"),
        ["FilamentVariant"] = Split("ColorName ColorHex FilamentId PricePerKg LowStockGrams LowStockDays"),
        ["Spool"] = Split("Label FilamentVariantId SpoolWeightGrams RemainingWeightGrams State FeedSlotId Price Supplier OrderNumber ReceivedOn RfidId HasUsageOverrun"),
        ["SpoolUsageRecord"] = Split("SpoolId GramsConsumed JobId Source RemainingAfterGrams Reason"),
        ["FeedUnit"] = Split("PrinterId Kind UnitIndex Name ReplacesFeedUnitId ArchivedAtUtc IsRevealed FeedingSlotId FeedingSelectionSource"),
        ["FeedSlot"] = Split("SlotPosition ExtruderIndex DeactivatedAtUtc LinkedRfidId"),
        ["ProductionRun"] = Split("Name DueDate Priority Status Notes"),
        ["ProductionDemand"] = Split("Mode QuantityBasis State ProductionRunId TargetKind TargetId TargetName Quantity RevisionMode DefaultAssignmentMode DefaultPinnedPrinterId BackfillPrinterCap BackfillCumulativeUnitCeiling"),
        ["RunLine"] = Split("PartId AssemblyId TargetQuantity Quantity QuantityMode Status"),
        ["PartRequirement"] = Split("UnitsPerSet RecipeActive RunLineId PartId LibraryPartId PartName Quantity"),
        ["PrintJob"] = Split("Name Status PrinterId PartRequirementId QueuePosition PlateQuantity AssignmentMode PinnedPrinterId FileRevisionHash FileRevisionNumber TargetUnits ObjectExecutionJson FilamentFollowUpDismissedAtUtc CancelRequestedAtUtc RequeuedAsJobId WrittenOffAtUtc StartedAtUtc EndedAtUtc"),
        ["ExternalPrint"] = Split("Name Status PrinterId StartedAtUtc EndedAtUtc"),
        ["Print"] = Split("Name Status PrinterId StartedAtUtc EndedAtUtc"),
        ["MachineProfile"] = Split("Name BaseProfileId RawDataJson IsSelectable"),
        ["ProcessProfile"] = Split("Name BaseProfileId RawDataJson IsSelectable"),
        ["FilamentProfile"] = Split("Name BaseProfileId RawDataJson IsSelectable"),
        ["MaintenanceTask"] = Split("Name Notes EveryPrintHours EveryDays EveryPrints WarnAtPercent"),
        ["MaintenanceEnrollment"] = Split("TaskId PrinterId CycleStartedAtUtc CarryHours CarryPrints"),
        ["MaintenanceLog"] = Split("EnrollmentId TaskId PrinterId TaskName PrinterName PerformedAtUtc PerformedByUserId PerformedByNote Notes Cost HoursAtCompletion PrintsAtCompletion"),
        ["FarmSettings"] = Split("DefaultSlicerEngine DefaultSlicerVersion CurrencyCode ElectricityPricePerKwh FailureAllowancePercent"),
        ["ServerSettings"] = Split("McpMode PublicUrl"),
        ["FeeProfile"] = Split("Name Fees"),
        ["PrinterModelCost"] = Split("Brand ModelCode PrinterPurchasePrice PrinterUpfrontCost LifetimeServiceCost LifetimePrintingHours AveragePrintingWatts"),
        ["ApplicationUser"] = Split("UserName Email DisplayName IsEnabled"),
        ["ApplicationRole"] = Split("Name Description"),
        ["IdentityUserRole<Guid>"] = Split("UserId RoleId"),
        ["PersonalAccessToken"] = Split("Name Permissions ExpiresAtUtc RevokedAtUtc"),
        ["ConnectionSecret"] = Split("PrinterSerialNumber EncryptedAccessCode"),
        ["IdentityUserRole`1"] = Split("UserId RoleId"),
        ["IdentityUserClaim`1"] = Split("UserId ClaimType ClaimValue"),
        ["IdentityRoleClaim`1"] = Split("RoleId ClaimType ClaimValue"),
        ["BrowserSession"] = Split("ClientId Scopes RevokedAtUtc"),
    };

    private static string[] Split(string fields) => fields.Split(' ');
    private static string TypeName(EntityEntry entry) => entry.Metadata.ClrType.Name;
    private static string EntityType(string type) => type switch
    {
        "ApplicationUser" => "User",
        "ApplicationRole" => "Group",
        "PersonalAccessToken" => "AccessToken",
        _ => type,
    };

    private static string CanonicalType(EntityEntry entry) => TypeName(entry) == "BrowserSession"
        && entry.Property("ClientId").CurrentValue is not null ? "ConnectedApp" : EntityType(TypeName(entry));

    internal static IReadOnlyList<ActivityLogEntry> Prepare(OpenFarmDbContext db, AuditContext? context, string correlationId)
    {
        db.ChangeTracker.DetectChanges();
        var tracked = db.ChangeTracker.Entries().ToArray();
        if (tracked.Any(e => e.Entity is ActivityLogEntry && e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Audit entries are append-only.");
        var roots = tracked.Where(e => e.Metadata.FindProperty("Id") is not null)
            .GroupBy(e => (Type: CanonicalType(e), Id: e.Property("Id").CurrentValue?.ToString() ?? ""))
            .ToDictionary(g => g.Key, g => g.First());
        var explicitEntries = tracked.Where(e => e.Entity is ActivityLogEntry && e.State == EntityState.Added)
            .Select(e => (ActivityLogEntry)e.Entity).ToList();
        var generated = new List<ActivityLogEntry>();
        var actorName = context?.ActorName;
        if (actorName is null && context?.ActorId is { } actorId)
            actorName = db.Users.AsNoTracking().Where(u => u.Id == actorId)
                .Select(u => u.DisplayName ?? u.UserName ?? u.Email).FirstOrDefault();

        foreach (var entry in tracked.Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var name = TypeName(entry);
            if (!Fields.TryGetValue(name, out var fields)) continue;
            // Vendor catalog refresh is reference data, not a farm edit.
            if (name.EndsWith("Profile", StringComparison.Ordinal) && entry.Metadata.FindProperty("Origin") is not null
                && entry.Property("Origin").CurrentValue?.ToString() == "System") continue;
            var changes = new List<AuditFieldChange>();
            foreach (var property in entry.Properties.Where(p => fields.Contains(p.Metadata.Name, StringComparer.Ordinal)))
            {
                // Machine readiness is an observation unless an operator deliberately changed it.
                if (name == "Printer" && property.Metadata.Name == "Status" && context?.ActorId is null) continue;
                if (name == "FeedUnit" && property.Metadata.Name is "FeedingSlotId" or "FeedingSelectionSource" && context?.ActorId is null) continue;
                // Ordinary browser login/refresh activity is outside the audit scope.
                if (name == "BrowserSession" && entry.State == EntityState.Added) continue;
                var rawBefore = entry.State == EntityState.Added ? null : property.OriginalValue;
                var rawAfter = entry.State == EntityState.Deleted ? null : property.CurrentValue;
                if (JsonSerializer.Serialize(rawBefore) == JsonSerializer.Serialize(rawAfter)) continue;
                var before = entry.State == EntityState.Added ? null : SafeValue(property.Metadata.Name, property.OriginalValue);
                var after = entry.State == EntityState.Deleted ? null : SafeValue(property.Metadata.Name, property.CurrentValue);
                changes.Add(new(property.Metadata.Name, before, after));
            }
            if (changes.Count == 0) continue;
            var type = CanonicalType(entry);
            var id = string.Join(":", entry.Properties.Where(p => p.Metadata.IsPrimaryKey())
                .Select(p => p.CurrentValue?.ToString() ?? p.OriginalValue?.ToString()));
            // Child mutations belong to their owning record, so its whole history remains queryable.
            var parent = name switch
            {
                "SpoolUsageRecord" => ("Spool", "SpoolId"),
                "FilamentVariant" => ("Filament", "FilamentId"),
                "PartRequirement" => ("ProductionDemand", "RunLineId"),
                "ProductionDemand" => ("ProductionRun", "ProductionRunId"),
                "FilamentProfileAssignment" => ("Filament", "FilamentId"),
                "BomLine" => ("Assembly", "AssemblyId"),
                "PartTagJoinRow" or "PartFilamentRequirement" => ("Part", "PartId"),
                "FeedSlot" => ("FeedUnit", "FeedUnitId"),
                "IdentityUserRole`1" or "IdentityUserClaim`1" => ("User", "UserId"),
                "IdentityRoleClaim`1" => ("Group", "RoleId"),
                _ => ((string Type, string Key)?)null,
            };
            if (parent is { } nullableOwner && entry.Metadata.FindProperty(nullableOwner.Key) is not null
                && entry.Property(nullableOwner.Key).CurrentValue is null) parent = null;
            if (parent is { } owner && entry.Metadata.FindProperty(owner.Key) is not null)
            {
                type = owner.Type;
                var childId = entry.Metadata.FindProperty("ClaimValue") is not null ? entry.Property("ClaimValue").CurrentValue?.ToString() ?? id
                    : name == "IdentityUserRole`1" ? entry.Property("RoleId").CurrentValue?.ToString() ?? id : id;
                id = entry.Property(owner.Key).CurrentValue?.ToString() ?? id;
                changes = changes.Select(c => c with { Field = $"{name}.{childId}.{c.Field}" }).ToList();
            }
            var printerIds = new List<Guid>();
            if (type == "Printer" && Guid.TryParse(id, out var printerId)) printerIds.Add(printerId);
            if (entry.Metadata.FindProperty("PrinterId") is not null && entry.Property("PrinterId").CurrentValue is Guid relatedPrinter) printerIds.Add(relatedPrinter);
            if (name == "MaintenanceTask" && entry.State != EntityState.Added && Guid.TryParse(id, out var taskId))
                printerIds.AddRange(db.MaintenanceEnrollments.AsNoTracking().Where(e => e.TaskId == taskId).Select(e => e.PrinterId));
            var action = parent is not null ? "update" : entry.State switch
            {
                EntityState.Added => "create",
                EntityState.Deleted => "delete",
                _ => "update",
            };
            if (parent is not null && roots.TryGetValue((type, id), out var root))
                action = root.State == EntityState.Added ? "create" : root.State == EntityState.Deleted ? "delete" : "update";
            var audit = explicitEntries.Concat(generated).FirstOrDefault(a => a.EntityType == type && a.EntityId == id);
            if (audit is null)
            {
                var eventType = (context?.EntityType == type || context?.EntityType == "Profile" && name is "MachineProfile" or "FilamentProfile" or "ProcessProfile") && context.EventType is { } semantic ? semantic
                    : type + (action == "create" ? "Created" : action == "delete" ? "Removed" : "Updated");
                if (printerIds.Count > 0 && context?.EventType?.StartsWith("Maintenance", StringComparison.Ordinal) == true)
                    eventType = context.EventType;
                if (type == "PrintJob" && changes.Any(c => c.Field == "Status") && context?.EntityType != type)
                    eventType = entry.Metadata.FindProperty("Status") is not null && entry.Property("Status").CurrentValue?.ToString() == "Cancelled"
                        ? "PrintJobCancelled" : "PrintJobStatusObserved";
                audit = ActivityLogEntry.Create(type, id, eventType, context?.ActorId,
                    context?.ActorId is null ? ActorType.System : ActorType.Operator);
                generated.Add(audit);
                db.ActivityLogEntries.Add(audit);
            }
            audit.AttributeInitiator(context?.InitiatedByUserId ?? (audit.ActorType == ActorType.System ? context?.ActorId : null));
            audit.RelatePrinters(printerIds);
            var existing = audit.ChangesJson is null ? [] : JsonSerializer.Deserialize<List<AuditFieldChange>>(audit.ChangesJson)!;
            var label = LabelFields
                .Where(f => entry.Metadata.FindProperty(f) is not null)
                .Select(f => (entry.State == EntityState.Deleted ? entry.Property(f).OriginalValue : entry.Property(f).CurrentValue)?.ToString())
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            if (parent is not null)
            {
                roots.TryGetValue((type, id), out var ownerEntry);
                label = ownerEntry?.Metadata.FindProperty("Name") is not null ? ownerEntry.Property("Name").CurrentValue?.ToString() : null;
            }
            audit.Describe(action, label, audit.ActorType == ActorType.System ? null : actorName, context?.Operation,
                context?.CorrelationId ?? correlationId, audit.ActorType == ActorType.System ? "System" : context?.Via?.Kind ?? "Browser",
                [.. existing, .. changes]);
            if (context?.Via is { } via) audit.AttributeTo(via);
        }
        foreach (var audit in explicitEntries.Concat(generated))
        {
            audit.AttributeInitiator(context?.InitiatedByUserId ?? (audit.ActorType == ActorType.System ? context?.ActorId : null));
            if (audit.Details?.InitiatedByUserId is { } initiator)
                audit.AttributeInitiator(initiator, db.Users.AsNoTracking().Where(u => u.Id == initiator).Select(u => u.DisplayName ?? u.UserName ?? u.Email).FirstOrDefault());
            var capturedName = audit.ActorId == context?.ActorId ? actorName : null;
            if (capturedName is null && audit.ActorId is { } id)
                capturedName = db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.DisplayName ?? u.UserName ?? u.Email).FirstOrDefault();
            roots.TryGetValue((audit.EntityType, audit.EntityId), out var entity);
            var label = entity is null ? null : LabelFields.Where(f => entity.Metadata.FindProperty(f) is not null)
                .Select(f => entity.Property(f).CurrentValue?.ToString()).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            if (audit.EntityType == "Printer" && Guid.TryParse(audit.EntityId, out var relatedId)) audit.RelatePrinters([relatedId]);
            audit.Describe("action", label, capturedName, context?.Operation, context?.CorrelationId ?? correlationId,
                audit.ActorType == ActorType.System ? "System" : context?.Via?.Kind ?? "Browser");
            if (context?.Via is { } via) audit.AttributeTo(via);
        }
        return generated;
    }

    internal static object? SafeValue(string field, object? value) => AuditFieldValue.Sanitize(field, value);
}
