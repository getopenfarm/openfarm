using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Parts;
using Handler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Parts.SetPartUnitsPerPrintCommand, OpenFarm.Domain.Parts.Part>;

namespace OpenFarm.Api.Endpoints.Parts;

public sealed class SetPartUnitsPerPrintRequest : IActivityLoggable
{
    public Guid Id { get; set; }
    public int UnitsPerPrint { get; set; }
    public string EntityType => "Part";
    public string EventType => "PartUnitsPerPrintChanged";
}

public sealed class SetPartUnitsPerPrintRequestValidator : AbstractValidator<SetPartUnitsPerPrintRequest>
{
    public SetPartUnitsPerPrintRequestValidator() => RuleFor(r => r.UnitsPerPrint).GreaterThanOrEqualTo(1);
}

[Permission("parts:replace-file")]
public sealed class SetPartUnitsPerPrintEndpoint(Handler handler) : Endpoint<SetPartUnitsPerPrintRequest, PartResponse>
{
    public override void Configure()
    {
        Patch("/api/v1/parts/{id}/units-per-print");
        Policies("Permission");
    }

    public override async Task HandleAsync(SetPartUnitsPerPrintRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        Response = PartResponse.FromDomain(await handler.HandleAsync(new SetPartUnitsPerPrintCommand(req.Id, req.UnitsPerPrint), ct));
    }
}
