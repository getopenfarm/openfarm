using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Api.ExceptionHandling;

/// <summary>
/// Maps typed domain/application exceptions to RFC 7807 <see cref="ProblemDetails"/> (CLAUDE.md:
/// "domain-level failures are typed domain exceptions caught by global exception-handling
/// middleware"). Exceptions not recognized here fall through to the framework's default handling.
/// </summary>
public sealed class DomainExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            Microsoft.EntityFrameworkCore.DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: "23503", ConstraintName: "FK_Parts_LibraryFolders_FolderId" or "FK_Assemblies_LibraryFolders_FolderId" } } => (StatusCodes.Status409Conflict, "Folder changed"),
            OpenFarm.Domain.Library.LibraryItemNotFoundException => (StatusCodes.Status404NotFound, "Library item not found"),
            OpenFarm.Domain.Library.LibraryFolderNotFoundException => (StatusCodes.Status404NotFound, "Folder not found"),
            OpenFarm.Domain.Library.LibraryConflictException => (StatusCodes.Status409Conflict, "Library conflict"),
            OpenFarm.Application.Common.InvalidListSortException => (StatusCodes.Status400BadRequest, "Invalid list sorting"),
            OpenFarm.Application.Production.ProductionPlanConflictException => (StatusCodes.Status409Conflict, "Production plan changed"),
            OpenFarm.Application.Production.InvalidProductionDemandException => (StatusCodes.Status422UnprocessableEntity, "Invalid production demand"),
            DuplicatePrinterSerialNumberException => (StatusCodes.Status409Conflict, "Duplicate printer serial number"),
            PrinterConnectionVerificationFailedException => (StatusCodes.Status422UnprocessableEntity, "Printer connection could not be verified"),
            PrinterModelCostsInUseException => (StatusCodes.Status409Conflict, "Printer model is still in the fleet"),
            UnknownPrinterModelException => (StatusCodes.Status422UnprocessableEntity, "Unknown printer model"),
            IncompatibleBuildPlateException => (StatusCodes.Status422UnprocessableEntity, "Build plate incompatible with printer model"),
            ImmutablePrinterFieldMismatchException => (StatusCodes.Status422UnprocessableEntity, "Printer identity fields cannot be changed"),
            InvalidPrinterStatusTransitionException => (StatusCodes.Status409Conflict, "Invalid printer status transition"),
            PrinterNotFoundException => (StatusCodes.Status404NotFound, "Printer not found"),
            ProcessProfileRequiresMachineProfileException => (StatusCodes.Status422UnprocessableEntity, "Machine profile required"),
            ProcessProfileIncompatibleWithMachineException => (StatusCodes.Status422UnprocessableEntity, "Process profile incompatible with machine"),
            InvalidProfileReferenceException => (StatusCodes.Status400BadRequest, "Invalid profile reference"),
            UnsupportedProfileBundleFormatException => (StatusCodes.Status422UnprocessableEntity, "Unsupported or corrupted profile bundle"),
            ProfileSelectionMismatchException => (StatusCodes.Status422UnprocessableEntity, "Selection does not match the uploaded bundle"),
            MachineProfileNotFoundException => (StatusCodes.Status404NotFound, "Machine profile not found"),
            FilamentProfileNotFoundException => (StatusCodes.Status404NotFound, "Filament profile not found"),
            ProcessProfileNotFoundException => (StatusCodes.Status404NotFound, "Process profile not found"),
            ProcessProfileSourceFormatMismatchException => (StatusCodes.Status422UnprocessableEntity, "Process profile source format mismatch"),
            InvalidProfileJsonException => (StatusCodes.Status422UnprocessableEntity, "Invalid profile JSON"),
            SystemProfileReadOnlyException => (StatusCodes.Status422UnprocessableEntity, "System profile is read-only"),
            VendorNotFoundException => (StatusCodes.Status404NotFound, "Vendor not found"),
            FilamentNotFoundException => (StatusCodes.Status404NotFound, "Filament not found"),
            FilamentVariantNotFoundException => (StatusCodes.Status404NotFound, "Filament variant not found"),
            SpoolNotFoundException => (StatusCodes.Status404NotFound, "Spool not found"),
            FeedSlotNotFoundException => (StatusCodes.Status404NotFound, "Feed slot not found"),
            FeedUnitNotFoundException => (StatusCodes.Status404NotFound, "Feed unit not found"),
            ExternalSpoolNeverReportedException => (StatusCodes.Status404NotFound, "External Spool never reported"),
            InvalidFeedUnitReplacementException => (StatusCodes.Status422UnprocessableEntity, "Invalid Feed Unit replacement target"),
            DuplicateVendorNameException => (StatusCodes.Status409Conflict, "Duplicate vendor name"),
            DuplicateFilamentVariantColorException => (StatusCodes.Status409Conflict, "Duplicate filament variant color"),
            InvalidReloaderConfigurationException => (StatusCodes.Status422UnprocessableEntity, "Invalid reloader configuration"),
            DuplicateAutomaticFilamentReloaderNameException => (StatusCodes.Status409Conflict, "Duplicate Automatic Filament Reloader name"),
            SpoolConcurrencyConflictException => (StatusCodes.Status409Conflict, "Spool was concurrently modified"),
            FeedSlotOccupiedException => (StatusCodes.Status409Conflict, "Feed slot already occupied"),
            InvalidSpoolStateTransitionException => (StatusCodes.Status422UnprocessableEntity, "Invalid spool state transition"),
            SpoolRfidConflictException => (StatusCodes.Status409Conflict, "RFID tag in use"),
            FeedSlotDeactivatedException => (StatusCodes.Status422UnprocessableEntity, "Feed slot is deactivated"),
            NotAnAutomaticFilamentReloaderException => (StatusCodes.Status422UnprocessableEntity, "Feed unit cannot be manually removed"),
            PartPrintRequirementsIncompleteException => (StatusCodes.Status422UnprocessableEntity, "Incomplete print requirements"),
            PartNotFoundException => (StatusCodes.Status404NotFound, "Part not found"),
            InvalidRevisionModeException => (StatusCodes.Status422UnprocessableEntity, "Revision behavior is fixed by quantity"),
            ProductionSnapshotUnavailableException => (StatusCodes.Status409Conflict, "Production snapshot unavailable"),
            PartDeletionBlockedException => (StatusCodes.Status409Conflict, "Part removal blocked"),
            PartReferencedException => (StatusCodes.Status409Conflict, "Part is still referenced"),
            PartFilamentRequirementNotFoundException => (StatusCodes.Status404NotFound, "Filament requirement not found"),
            TagNotFoundException => (StatusCodes.Status404NotFound, "Tag not found"),
            PartProcessingJobNotFoundException => (StatusCodes.Status404NotFound, "Part processing job not found"),
            DuplicatePartTagException => (StatusCodes.Status409Conflict, "Tag already associated with this part"),
            UnsupportedPartFileFormatException => (StatusCodes.Status400BadRequest, "Unrecognized part file format"),
            PartFileParsingFailedException => (StatusCodes.Status422UnprocessableEntity, "Could not parse the file's metadata"),
            SlicingFailedException => (StatusCodes.Status422UnprocessableEntity, "Slicing failed"),
            SlicerUnavailableException => (StatusCodes.Status422UnprocessableEntity, "Slicer unavailable"),
            SlicingProfileUnconfiguredException => (StatusCodes.Status422UnprocessableEntity, "Slicing profile unconfigured"),
            NoNozzleDiameterAvailableException => (StatusCodes.Status422UnprocessableEntity, "No printer with a known nozzle diameter is registered"),
            UnslicedProjectMaterialParsingFailedException => (StatusCodes.Status422UnprocessableEntity, "Could not parse the project's filament assignment"),
            OpenFarm.Application.Parts.InvalidPartFilamentSelectionException => (StatusCodes.Status400BadRequest, "Invalid filament selection"),
            InvalidConfirmPartProcessingJobRequestException => (StatusCodes.Status400BadRequest, "Invalid confirm request"),
            PartProcessingJobNotReadyForReviewException => (StatusCodes.Status409Conflict, "Part processing job is not ready for review"),
            PartConcurrencyConflictException => (StatusCodes.Status409Conflict, "Part was concurrently modified"),
            AssemblyNotFoundException => (StatusCodes.Status404NotFound, "Assembly not found"),
            NonPrintedComponentNotFoundException => (StatusCodes.Status404NotFound, "Non-printed component not found"),
            BomLineNotFoundException => (StatusCodes.Status404NotFound, "BOM line not found"),
            AssemblyMustHaveAtLeastOneLineException => (StatusCodes.Status422UnprocessableEntity, "Assembly must have at least one BOM line"),
            AssemblyCycleException => (StatusCodes.Status422UnprocessableEntity, "Saving this assembly would create a cycle"),
            AssemblyDepthLimitExceededException => (StatusCodes.Status422UnprocessableEntity, "Saving this assembly would exceed the maximum nesting depth"),
            DuplicateBomLineTargetException => (StatusCodes.Status409Conflict, "This target already appears on the assembly"),
            AssemblyConcurrencyConflictException => (StatusCodes.Status409Conflict, "Assembly was concurrently modified"),
            NonPrintedComponentConcurrencyConflictException => (StatusCodes.Status409Conflict, "Non-printed component was concurrently modified"),
            AssemblyReferencedException => (StatusCodes.Status409Conflict, "Assembly is still referenced by other assemblies"),
            PartReferencedByAssemblyException => (StatusCodes.Status409Conflict, "Part is still referenced by an assembly"),
            NonPrintedComponentReferencedException => (StatusCodes.Status409Conflict, "Non-printed component is still referenced by an assembly"),
            InvalidExplosionQuantityException => (StatusCodes.Status400BadRequest, "Invalid explosion quantity"),
            InvalidBomLineQuantityException => (StatusCodes.Status400BadRequest, "Invalid BOM line quantity"),
            ProductionRunNotFoundException => (StatusCodes.Status404NotFound, "Production run not found"),
            RunLineNotFoundException => (StatusCodes.Status404NotFound, "Run line not found"),
            PrintJobNotFoundException => (StatusCodes.Status404NotFound, "Print job not found"),
            RunLineTargetNotFoundException => (StatusCodes.Status404NotFound, "Run line target not found"),
            RunLineMustTargetExactlyOnePartOrAssemblyException => (StatusCodes.Status400BadRequest, "A run line must target exactly one Part or Assembly"),
            ProductionRunMustHaveAtLeastOneLineException => (StatusCodes.Status422UnprocessableEntity, "A production run must have at least one line"),
            InvalidPrintJobStatusTransitionException => (StatusCodes.Status422UnprocessableEntity, "Invalid print job status transition"),
            ProductionRunNotOpenException => (StatusCodes.Status409Conflict, "Production run is closed or cancelled"),
            RunLineHasNoPrintedPartsException => (StatusCodes.Status422UnprocessableEntity, "Assembly has no printed parts"),
            PrintJobNotEligibleForWriteOffException => (StatusCodes.Status409Conflict, "Print job is not eligible for write-off"),
            InvalidRunCompletionException => (StatusCodes.Status409Conflict, "Invalid production run status transition"),
            PinnedJobRequiresPrinterException => (StatusCodes.Status400BadRequest, "A pinned job requires a printer id"),
            PrintJobAlreadyDispatchedException => (StatusCodes.Status409Conflict, "Print job is no longer queued"),
            ProductionRunConcurrencyConflictException => (StatusCodes.Status409Conflict, "Production run or print job was concurrently modified"),
            FilamentConsumptionSumMismatchException => (StatusCodes.Status400BadRequest, "Filament consumption does not match a requirement on this job"),
            JobNotEligibleForRemoteControlException => (StatusCodes.Status409Conflict, "Print job is not eligible for this remote-control action"),
            PrintJobAlreadyRequeuedException => (StatusCodes.Status409Conflict, "Print job has already been requeued"),
            PrintFilamentNotAssignableException => (StatusCodes.Status409Conflict, "A spool cannot be assigned to this filament line"),
            FilamentUsageNotEstimableException => (StatusCodes.Status422UnprocessableEntity, "Filament usage cannot be estimated"),
            PrintJobNotEligibleForRequeueException => (StatusCodes.Status409Conflict, "Print job is not eligible for requeue"),
            RunLineNotEligibleForBackfillLimitsException => (StatusCodes.Status409Conflict, "Run line is not unlimited, so backfill limits don't apply"),
            // Backstop for domain invariant guards (blank names, weight above capacity, ...) not caught by a request validator.
            ArgumentException => (StatusCodes.Status422UnprocessableEntity, "Invalid request"),
            _ => (0, null),
        };

        if (statusCode == 0)
        {
            return false;
        }

        var extensions = new Dictionary<string, object?>();
        if (exception is PartDeletionBlockedException blocked)
        {
            extensions["code"] = "PartDeletionBlocked";
            extensions["blockers"] = blocked.Eligibility.Blockers;
        }
        else if (exception is PartConcurrencyConflictException) extensions["code"] = "PartConcurrencyConflict";
        else if (exception is PartReferencedException or PartReferencedByAssemblyException) extensions["code"] = "PartDependenciesChanged";
        else if (exception is ProfileSetBlockedException setBlocked)
        {
            extensions["code"] = setBlocked.Code;
            extensions["issues"] = setBlocked.Issues.Select(issue => new { kind = issue.Kind?.ToString(), code = issue.Code.ToString(), name = issue.Name }).ToList();
        }
        else if (exception is SlicingProfileUnconfiguredException { Code: { } profileCode }) extensions["code"] = profileCode;
        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Type = $"https://httpstatuses.io/{statusCode}",
                Title = title,
                Status = statusCode,
                Detail = exception.Message,
                Extensions = extensions,
            },
            cancellationToken);

        return true;
    }
}
