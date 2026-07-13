using FastEndpoints;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Printers;
using MarkPrinterReadyHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Printers.MarkPrinterReadyCommand, OpenFarm.Domain.Printers.Printer>;

namespace OpenFarm.Api.Endpoints.Printers;

public sealed class MarkPrinterReadyRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string EntityType => "Printer";

    public string EventType => "PrinterMarkedReady";
}

/// <summary>
/// "Mark ready" per <c>contracts/printers-api.md</c> — the only manual status transition this
/// phase supports (<c>AwaitingClear → Idle</c>). Illegal transitions surface as 409 via
/// <see cref="ExceptionHandling.DomainExceptionHandler"/>.
/// </summary>
[Permission("printers:mark-ready")]
public sealed class MarkPrinterReadyEndpoint(MarkPrinterReadyHandler handler)
    : Endpoint<MarkPrinterReadyRequest, PrinterSummaryResponse>
{
    public override void Configure()
    {
        Post("/api/v1/printers/{id}/mark-ready");
        Policies("Permission");
    }

    public override async Task HandleAsync(MarkPrinterReadyRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");

        var printer = await handler.HandleAsync(new MarkPrinterReadyCommand(req.Id), ct);

        Response = PrinterSummaryResponse.FromDomain(printer);
    }
}
