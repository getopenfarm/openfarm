using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;
using UpdateHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.UpdateFilamentProfileAssignmentsCommand, OpenFarm.Domain.FilamentTracking.Filament>;

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

public sealed class UpdateFilamentProfileAssignmentsRequest : IActivityLoggable
{
    public IReadOnlyList<FilamentProfileAssignmentInput> Assignments { get; set; } = [];
    public string EntityType => "Filament";
    public string EventType => "FilamentProfileAssignmentsUpdated";
}

public sealed class UpdateFilamentProfileAssignmentsValidator : Validator<UpdateFilamentProfileAssignmentsRequest>
{
    public UpdateFilamentProfileAssignmentsValidator() => RuleFor(x => x.Assignments).NotNull();
}

[Permission("filaments:assign-profiles")]
public sealed class UpdateFilamentProfileAssignmentsEndpoint(UpdateHandler handler)
    : Endpoint<UpdateFilamentProfileAssignmentsRequest, FilamentProfileAssignmentsResponse>
{
    public override void Configure() { Put("/api/v1/filaments/{id}/profile-assignments"); Policies("Permission"); }
    public override async Task HandleAsync(UpdateFilamentProfileAssignmentsRequest req, CancellationToken ct)
    {
        var filament = await handler.HandleAsync(new(Route<Guid>("id"), req.Assignments), ct);
        Response = new() { Assignments = FilamentResponse.FromDomain(filament, "").ProfileAssignments };
    }
}
