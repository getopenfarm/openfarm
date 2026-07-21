using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;

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
            DuplicatePrinterSerialNumberException => (StatusCodes.Status409Conflict, "Duplicate printer serial number"),
            PrinterConnectionVerificationFailedException => (StatusCodes.Status422UnprocessableEntity, "Printer connection could not be verified"),
            UnknownPrinterModelException => (StatusCodes.Status422UnprocessableEntity, "Unknown printer model"),
            ImmutablePrinterFieldMismatchException => (StatusCodes.Status422UnprocessableEntity, "Printer identity fields cannot be changed"),
            InvalidPrinterStatusTransitionException => (StatusCodes.Status409Conflict, "Invalid printer status transition"),
            PrinterNotFoundException => (StatusCodes.Status404NotFound, "Printer not found"),
            UnsupportedProfileBundleFormatException => (StatusCodes.Status422UnprocessableEntity, "Unsupported or corrupted profile bundle"),
            ProfileSelectionMismatchException => (StatusCodes.Status422UnprocessableEntity, "Selection does not match the uploaded bundle"),
            MachineProfileNotFoundException => (StatusCodes.Status404NotFound, "Machine profile not found"),
            FilamentProfileNotFoundException => (StatusCodes.Status404NotFound, "Filament profile not found"),
            ProcessProfileNotFoundException => (StatusCodes.Status404NotFound, "Process profile not found"),
            ProcessProfileSourceFormatMismatchException => (StatusCodes.Status422UnprocessableEntity, "Process profile source format mismatch"),
            InvalidProfileJsonException => (StatusCodes.Status422UnprocessableEntity, "Invalid profile JSON"),
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
            DuplicateAutomaticFilamentReloaderNameException => (StatusCodes.Status409Conflict, "Duplicate Automatic Filament Reloader name"),
            SpoolConcurrencyConflictException => (StatusCodes.Status409Conflict, "Spool was concurrently modified"),
            FeedSlotOccupiedException => (StatusCodes.Status409Conflict, "Feed slot already occupied"),
            InvalidSpoolStateTransitionException => (StatusCodes.Status422UnprocessableEntity, "Invalid spool state transition"),
            FeedSlotDeactivatedException => (StatusCodes.Status422UnprocessableEntity, "Feed slot is deactivated"),
            NotAnAutomaticFilamentReloaderException => (StatusCodes.Status422UnprocessableEntity, "Feed unit cannot be manually removed"),
            PartNotFoundException => (StatusCodes.Status404NotFound, "Part not found"),
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
            InvalidConfirmPartProcessingJobRequestException => (StatusCodes.Status400BadRequest, "Invalid confirm request"),
            PartProcessingJobNotReadyForReviewException => (StatusCodes.Status409Conflict, "Part processing job is not ready for review"),
            PartConcurrencyConflictException => (StatusCodes.Status409Conflict, "Part was concurrently modified"),
            _ => (0, null),
        };

        if (statusCode == 0)
        {
            return false;
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Type = $"https://httpstatuses.io/{statusCode}",
                Title = title,
                Status = statusCode,
                Detail = exception.Message,
            },
            cancellationToken);

        return true;
    }
}
