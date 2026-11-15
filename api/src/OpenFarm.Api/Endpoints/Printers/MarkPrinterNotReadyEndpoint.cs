using FastEndpoints;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Printers;
using MarkPrinterNotReadyHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Printers.MarkPrinterNotReadyCommand, OpenFarm.Domain.Printers.Printer>;

namespace OpenFarm.Api.Endpoints.Printers;

public sealed class MarkPrinterNotReadyRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string EntityType => "Printer";

    public string EventType => "PrinterMarkedNotReady";
}

/// <summary>
/// "Mark not ready": an operator hold on an Idle printer (<c>Idle → AwaitingClear</c>). Shares the
/// <c>printers:mark-ready</c> permission with <see cref="MarkPrinterReadyEndpoint"/>: both set whether the printer
/// takes jobs. Illegal transitions surface as 409 via <see cref="ExceptionHandling.DomainExceptionHandler"/>.
/// </summary>
[Permission("printers:mark-ready")]
public sealed class MarkPrinterNotReadyEndpoint(MarkPrinterNotReadyHandler handler)
    : Endpoint<MarkPrinterNotReadyRequest, PrinterSummaryResponse>
{
    public override void Configure()
    {
        Post("/api/v1/printers/{id}/mark-not-ready");
        Policies("Permission");
    }

    public override async Task HandleAsync(MarkPrinterNotReadyRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");

        var printer = await handler.HandleAsync(new MarkPrinterNotReadyCommand(req.Id), ct);

        Response = PrinterSummaryResponse.FromDomain(printer);
    }
}
