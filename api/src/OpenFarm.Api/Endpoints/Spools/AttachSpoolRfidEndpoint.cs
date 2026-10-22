using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;
using AttachSpoolRfidHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.AttachSpoolRfidCommand, OpenFarm.Domain.FilamentTracking.Spool>;

namespace OpenFarm.Api.Endpoints.Spools;

public sealed class AttachSpoolRfidRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string RfidId { get; set; } = string.Empty;

    public string EntityType => "Spool";

    public string EventType => "SpoolRfidLinked";
}

public sealed class AttachSpoolRfidRequestValidator : AbstractValidator<AttachSpoolRfidRequest>
{
    public AttachSpoolRfidRequestValidator()
    {
        RuleFor(x => x.RfidId).NotEmpty().MaximumLength(Spool.MaxRfidIdLength);
    }
}

/// <summary>Ties an untagged spool to the RFID tag a printer reads. 409 when another active spool has the tag, or this one has another.</summary>
[Permission("spools:load")]
public sealed class AttachSpoolRfidEndpoint(AttachSpoolRfidHandler handler, IQueryHandler<GetSpoolsQuery, IReadOnlyList<SpoolSummary>> spoolsQuery)
    : Endpoint<AttachSpoolRfidRequest, SpoolResponse>
{
    public override void Configure()
    {
        Put("/api/v1/spools/{id}/rfid");
        Policies("Permission");
    }

    public override async Task HandleAsync(AttachSpoolRfidRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");

        var spool = await handler.HandleAsync(new AttachSpoolRfidCommand(req.Id, req.RfidId), ct);

        Response = await SpoolResponseLookup.ResolveAsync(spoolsQuery, spool.Id, ct);
    }
}
