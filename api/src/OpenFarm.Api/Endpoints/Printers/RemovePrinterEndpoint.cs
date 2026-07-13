using FastEndpoints;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Printers;
using RemovePrinterHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Printers.RemovePrinterCommand, bool>;

namespace OpenFarm.Api.Endpoints.Printers;

public sealed class RemovePrinterRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string EntityType => "Printer";

    public string EventType => "PrinterRemoved";
}

/// <summary>
/// Remove a printer. Never blocked by current print status (FR-002/clarification) — the frontend
/// shows a confirmation warning before calling this; the endpoint performs no status check.
/// </summary>
[Permission("printers:remove")]
public sealed class RemovePrinterEndpoint(RemovePrinterHandler handler) : Endpoint<RemovePrinterRequest>
{
    public override void Configure()
    {
        Delete("/api/v1/printers/{id}");
        Policies("Permission");
    }

    public override async Task HandleAsync(RemovePrinterRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        await handler.HandleAsync(new RemovePrinterCommand(req.Id), ct);

        await HttpContext.Response.SendNoContentAsync(ct);
    }
}
