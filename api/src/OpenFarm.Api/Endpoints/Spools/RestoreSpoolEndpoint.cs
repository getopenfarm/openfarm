using FastEndpoints;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;
using RestoreSpoolHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.RestoreSpoolCommand, OpenFarm.Domain.FilamentTracking.Spool>;

namespace OpenFarm.Api.Endpoints.Spools;

public sealed class RestoreSpoolRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string EntityType => "Spool";

    public string EventType => "SpoolRestored";
}

/// <summary>A finished spool back to storage. 422 when it isn't finished; 409 when a concurrent change lost the race.</summary>
[Permission("spools:unload")]
public sealed class RestoreSpoolEndpoint(RestoreSpoolHandler handler, IQueryHandler<GetSpoolsQuery, IReadOnlyList<SpoolSummary>> spoolsQuery)
    : Endpoint<RestoreSpoolRequest, SpoolResponse>
{
    public override void Configure()
    {
        Put("/api/v1/spools/{id}/restore");
        Policies("Permission");
    }

    public override async Task HandleAsync(RestoreSpoolRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");

        var spool = await handler.HandleAsync(new RestoreSpoolCommand(req.Id), ct);

        Response = await SpoolResponseLookup.ResolveAsync(spoolsQuery, spool.Id, ct);
    }
}
