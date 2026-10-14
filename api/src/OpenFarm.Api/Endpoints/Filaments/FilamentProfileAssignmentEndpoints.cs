using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Api.Endpoints.Filaments;

[Permission("filaments:list")]
public sealed class GetFilamentAssignmentFleetEndpoint(FilamentProfileAssignmentService service)
    : EndpointWithoutRequest<FilamentAssignmentFleet>
{
    public override void Configure() { Get("/api/v1/filaments/profile-assignment-fleet"); Policies("Permission"); }
    public override async Task HandleAsync(CancellationToken ct) => Response = await service.GetFleetAsync(ct);
}

public sealed class FilamentProfileAssignmentsResponse
{
    public IReadOnlyList<FilamentProfileAssignmentInput> Assignments { get; set; } = [];
    public IReadOnlyList<FilamentProfileAssignmentIssue> Issues { get; set; } = [];
}

[Permission("filaments:list")]
public sealed class GetFilamentProfileAssignmentsEndpoint(IFilamentRepository repository, FilamentProfileAssignmentService service)
    : EndpointWithoutRequest<FilamentProfileAssignmentsResponse>
{
    public override void Configure() { Get("/api/v1/filaments/{id}/profile-assignments"); Policies("Permission"); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var filament = await repository.GetByIdAsync(id, ct) ?? throw new FilamentNotFoundException(id);
        Response = new() { Assignments = FilamentResponse.FromDomain(filament, "").ProfileAssignments, Issues = await service.GetIssuesAsync(filament, ct) };
    }
}
