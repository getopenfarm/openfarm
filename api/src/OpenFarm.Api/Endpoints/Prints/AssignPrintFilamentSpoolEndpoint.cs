using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Production;
using AssignSpoolHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.AssignPrintFilamentSpoolCommand, OpenFarm.Application.Production.AssignPrintFilamentSpoolResult>;

namespace OpenFarm.Api.Endpoints.Prints;

public sealed class AssignPrintFilamentSpoolRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public Guid LineId { get; set; }

    public Guid SpoolId { get; set; }

    public string EntityType => "Print";

    public string EventType => "PrintFilamentSpoolAssigned";
}

public sealed class AssignPrintFilamentSpoolValidator : Validator<AssignPrintFilamentSpoolRequest>
{
    public AssignPrintFilamentSpoolValidator() => RuleFor(x => x.SpoolId).NotEmpty();
}

public sealed class AssignPrintFilamentSpoolResponse
{
    /// <summary>The print's id (the activity log reads it as the entity id).</summary>
    public Guid Id { get; set; }

    public Guid LineId { get; set; }

    public Guid SpoolId { get; set; }

    public decimal GramsDeducted { get; set; }
}

/// <summary>
/// POST /api/v1/prints/{id}/filament-lines/{lineId}/spool — binds the spool to a closed print's unrecorded filament line and
/// deducts it, as automatic deduction would have. The spool must be loaded.
/// </summary>
[Permission("spools:record-usage")]
public sealed class AssignPrintFilamentSpoolEndpoint(AssignSpoolHandler handler)
    : Endpoint<AssignPrintFilamentSpoolRequest, AssignPrintFilamentSpoolResponse>
{
    public override void Configure()
    {
        Post("/api/v1/prints/{id}/filament-lines/{lineId}/spool");
        Policies("Permission");
    }

    public override async Task HandleAsync(AssignPrintFilamentSpoolRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        req.LineId = Route<Guid>("lineId");
        var result = await handler.HandleAsync(new AssignPrintFilamentSpoolCommand(req.Id, req.LineId, req.SpoolId), ct);
        Response = new AssignPrintFilamentSpoolResponse
        {
            Id = result.PrintId,
            LineId = result.LineId,
            SpoolId = result.SpoolId,
            GramsDeducted = result.GramsDeducted,
        };
    }
}
