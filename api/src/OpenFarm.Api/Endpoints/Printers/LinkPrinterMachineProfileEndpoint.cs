using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Printers;
using LinkMachineProfileHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Printers.LinkPrinterMachineProfileCommand, OpenFarm.Domain.Printers.Printer>;

namespace OpenFarm.Api.Endpoints.Printers;

public sealed class LinkPrinterMachineProfileRequest
{
    public Guid? MachineProfileId { get; set; }
}

public sealed class LinkPrinterMachineProfileResponse
{
    public Guid PrinterId { get; set; }
    public Guid? MachineProfileId { get; set; }
}

// PUT /api/v1/printers/{id}/machine-profile (contracts/part-library-api.md).
[Permission("printers:link-machine-profile")]
public sealed class LinkPrinterMachineProfileEndpoint(LinkMachineProfileHandler handler)
    : Endpoint<LinkPrinterMachineProfileRequest, LinkPrinterMachineProfileResponse>
{
    public override void Configure()
    {
        Put("/api/v1/printers/{id}/machine-profile");
        Policies("Permission");
    }

    public override async Task HandleAsync(LinkPrinterMachineProfileRequest req, CancellationToken ct)
    {
        var printerId = Route<Guid>("id");
        var printer = await handler.HandleAsync(new LinkPrinterMachineProfileCommand(printerId, req.MachineProfileId), ct);

        Response = new LinkPrinterMachineProfileResponse { PrinterId = printer.Id, MachineProfileId = printer.MachineProfileId };
    }
}
