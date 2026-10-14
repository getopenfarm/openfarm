using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;
using EditSpoolHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.EditSpoolCommand, OpenFarm.Domain.FilamentTracking.Spool>;

namespace OpenFarm.Api.Endpoints.Spools;

public sealed class EditSpoolRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string? Label { get; set; }

    public decimal RemainingWeightGrams { get; set; }

    public string EntityType => "Spool";

    public string EventType => "SpoolEdited";
}

public sealed class EditSpoolRequestValidator : AbstractValidator<EditSpoolRequest>
{
    public EditSpoolRequestValidator() => RuleFor(x => x.RemainingWeightGrams).GreaterThanOrEqualTo(0);
}

/// <summary>PUT /api/v1/spools/{id} — label and remaining weight in one save. 422 when the weight exceeds the spool's capacity.</summary>
[Permission("spools:override-weight")]
public sealed class EditSpoolEndpoint(EditSpoolHandler handler, IQueryHandler<GetSpoolsQuery, IReadOnlyList<SpoolSummary>> spoolsQuery)
    : Endpoint<EditSpoolRequest, SpoolResponse>
{
    public override void Configure()
    {
        Put("/api/v1/spools/{id}");
        Policies("Permission");
    }

    public override async Task HandleAsync(EditSpoolRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        var spool = await handler.HandleAsync(new EditSpoolCommand(req.Id, req.Label, req.RemainingWeightGrams), ct);

        Response = await SpoolResponseLookup.ResolveAsync(spoolsQuery, spool.Id, ct);
    }
}
