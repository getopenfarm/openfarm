using FastEndpoints;
using Microsoft.AspNetCore.SignalR;
using OpenFarm.Api.Hubs;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Production;

namespace OpenFarm.Api.Processors;

public sealed class ActivityLogPostProcessor : IGlobalPostProcessor
{
    public async Task PostProcessAsync(IPostProcessorContext context, CancellationToken ct)
    {
        if (context.HasExceptionOccurred || context.HasValidationFailures)
        {
            return;
        }

        var httpContext = context.HttpContext;

        if (httpContext.Response.StatusCode is < 200 or >= 300)
        {
            return;
        }

        if (context.Request is not IActivityLoggable loggable)
        {
            return;
        }

        await ActivityRecorder.RecordAsync(httpContext, loggable, context.Response, ct);
    }
}

/// <summary>
/// Publishes operational refreshes after a successful state-changing request. Persistence captures
/// the audit rows before commit. REST and MCP share these notifications.
/// </summary>
public static class ActivityRecorder
{
    public static async Task RecordAsync(HttpContext context, IActivityLoggable loggable, object? response, CancellationToken ct)
    {
        // Persistence captures changes atomically. This processor only publishes operational refreshes.
        if (response is IHasEntityIds ids)
        {
            foreach (var id in ids.EntityIds)
                await NotifyPrinterAsync(context, loggable.EntityType, id, ct);
        }
        else if ((ExtractEntityId(response) ?? ExtractEntityId(loggable)) is { } id)
        {
            await NotifyPrinterAsync(context, loggable.EntityType, id, ct);
        }
        await NotifyInventoryChangedAsync(context, loggable, ct);
        await NotifyMaintenanceChangedAsync(context, loggable, ct);
    }

    private static Task NotifyPrinterAsync(HttpContext context, string type, string id, CancellationToken ct) =>
        type == "Printer" && Guid.TryParse(id, out var printerId)
            ? context.RequestServices.GetRequiredService<IPrinterEventPublisher>().PrinterActivityChangedAsync(printerId, ct)
            : Task.CompletedTask;

    // A spool or filament change moves stock and what Action required lists; every open view refreshes on this one event.
    private static Task NotifyInventoryChangedAsync(HttpContext context, IActivityLoggable loggable, CancellationToken ct) =>
        loggable.EntityType is "Spool" or "Filament"
            ? context.RequestServices.GetRequiredService<IProductionEventPublisher>().NotificationsChangedAsync(ct)
            : Task.CompletedTask;

    // Every maintenance mutation refreshes the maintenance views; due state itself is computed on read.
    private static Task NotifyMaintenanceChangedAsync(HttpContext context, IActivityLoggable loggable, CancellationToken ct) =>
        loggable.EventType.StartsWith("Maintenance", StringComparison.Ordinal)
            ? context.RequestServices.GetRequiredService<IFarmEventClients>().SendAsync("MaintenanceChanged", new { }, ct)
            : Task.CompletedTask;

    private static string? ExtractEntityId(object? response) =>
        response?.GetType().GetProperty("Id")?.GetValue(response)?.ToString();

}
