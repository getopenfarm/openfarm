using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Common;
using EditAssemblyHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Assemblies.EditAssemblyCommand, OpenFarm.Domain.Assemblies.Assembly>;

namespace OpenFarm.Api.Endpoints.Assemblies;

public sealed class EditAssemblyRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string EntityType => "Assembly";

    public string EventType => "AssemblyEdited";
}

public sealed class EditAssemblyRequestValidator : AbstractValidator<EditAssemblyRequest>
{
    public EditAssemblyRequestValidator() => RuleFor(x => x.Name).NotEmpty();
}

/// <summary>PUT /api/v1/assemblies/{id} — renames/redescribes only; line changes go through the line endpoints (contracts/assembly-bom-api.md).</summary>
[Permission("assemblies:edit")]
public sealed class EditAssemblyEndpoint(EditAssemblyHandler handler, IQueryHandler<GetAssemblyQuery, AssemblyDetail?> getAssembly)
    : Endpoint<EditAssemblyRequest, AssemblyResponse>
{
    public override void Configure()
    {
        Put("/api/v1/assemblies/{id}");
        Policies("Permission");
    }

    public override async Task HandleAsync(EditAssemblyRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        await handler.HandleAsync(new EditAssemblyCommand(req.Id, req.Name, req.Description), ct);

        var detail = await getAssembly.HandleAsync(new GetAssemblyQuery(req.Id), ct);
        Response = AssemblyResponse.FromDetail(detail!);
    }
}
