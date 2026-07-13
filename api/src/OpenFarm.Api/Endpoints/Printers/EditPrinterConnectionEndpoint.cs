using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;
using EditPrinterConnectionHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Printers.EditPrinterConnectionCommand, OpenFarm.Domain.Printers.Printer>;

namespace OpenFarm.Api.Endpoints.Printers;

public sealed class EditPrinterConnectionRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string IpAddress { get; set; } = string.Empty;

    public string AccessCode { get; set; } = string.Empty;

    public string EntityType => "Printer";

    public string EventType => "PrinterConnectionEdited";
}

public sealed class EditPrinterConnectionRequestValidator : AbstractValidator<EditPrinterConnectionRequest>
{
    public EditPrinterConnectionRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.IpAddress).NotEmpty();
        RuleFor(x => x.AccessCode).NotEmpty();
    }
}

/// <summary>
/// Edit connection details (IP/access code) on an existing printer — same verify-before-save gate
/// as registration (FR-002).
/// </summary>
[Permission("printers:edit-connection")]
public sealed class EditPrinterConnectionEndpoint(EditPrinterConnectionHandler handler)
    : Endpoint<EditPrinterConnectionRequest, PrinterSummaryResponse>
{
    public override void Configure()
    {
        Put("/api/v1/printers/{id}/connection");
        Policies("Permission");
    }

    public override async Task HandleAsync(EditPrinterConnectionRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");

        var command = new EditPrinterConnectionCommand(req.Id, req.IpAddress, req.AccessCode);
        var printer = await handler.HandleAsync(command, ct);

        Response = PrinterSummaryResponse.FromDomain(printer);
    }
}
