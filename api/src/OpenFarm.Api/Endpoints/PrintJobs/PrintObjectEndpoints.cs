using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;

namespace OpenFarm.Api.Endpoints.PrintJobs;

public sealed class PartObjectsRequest { public Guid Id { get; set; } }
[Permission("parts:view")]
public sealed class GetPartObjectsEndpoint(PrintObjectControlService objects) : Endpoint<PartObjectsRequest, PrintObjectManifest>
{
    public override void Configure() { Get("/api/v1/parts/{id}/objects"); Policies("Permission"); }
    public override async Task HandleAsync(PartObjectsRequest req, CancellationToken ct) => Response = await objects.ReadPartAsync(Route<Guid>("id"), ct);
}
public sealed class SetPartObjectGroupsRequest : IActivityLoggable
{
    public Guid Id { get; set; }
    public string RevisionHash { get; set; } = "";
    public List<PrintUnit> Units { get; set; } = [];
    public string EntityType => "Part";
    public string EventType => "PartObjectGroupsChanged";
}
public sealed class SetPartObjectGroupsValidator : Validator<SetPartObjectGroupsRequest>
{
    public SetPartObjectGroupsValidator() { RuleFor(r => r.RevisionHash).NotEmpty(); RuleFor(r => r.Units).NotEmpty().Must(u => u is { Count: <= 10000 }); }
}
[Permission("parts:edit")]
public sealed class SetPartObjectGroupsEndpoint(PrintObjectControlService objects) : Endpoint<SetPartObjectGroupsRequest, PrintObjectManifest>
{
    public override void Configure() { Put("/api/v1/parts/{id}/object-groups"); Policies("Permission"); }
    public override async Task HandleAsync(SetPartObjectGroupsRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        Response = await objects.SaveGroupsAsync(req.Id, req.RevisionHash, req.Units, ct);
    }
}
public sealed class JobObjectsRequest { public Guid Id { get; set; } }
[Permission("print-jobs:view")]
public sealed class GetJobObjectsEndpoint(PrintObjectControlService objects) : Endpoint<JobObjectsRequest, PrintObjectExecution?>
{
    public override void Configure() { Get("/api/v1/print-jobs/{id}/objects"); Policies("Permission"); }
    public override async Task HandleAsync(JobObjectsRequest req, CancellationToken ct) => Response = await objects.ReadJobAsync(Route<Guid>("id"), ct);
}
public sealed class SkipJobObjectsRequest : IActivityLoggable
{
    public Guid Id { get; set; }
    public string ArtifactHash { get; set; } = "";
    public List<string> UnitIds { get; set; } = [];
    public string EntityType => "PrintJob";
    public string EventType => "PrintJobObjectSkipRequested";
}
public sealed class SkipJobObjectsValidator : Validator<SkipJobObjectsRequest>
{
    public SkipJobObjectsValidator() { RuleFor(r => r.ArtifactHash).NotEmpty(); RuleFor(r => r.UnitIds).NotEmpty().Must(ids => ids is { Count: <= 10000 }); }
}
[Permission("print-jobs:remote-control")]
public sealed class SkipJobObjectsEndpoint(PrintObjectControlService objects) : Endpoint<SkipJobObjectsRequest, PrintObjectExecution>
{
    public override void Configure() { Post("/api/v1/print-jobs/{id}/skip-objects"); Policies("Permission"); }
    public override async Task HandleAsync(SkipJobObjectsRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        Response = await objects.SkipAsync(req.Id, req.ArtifactHash, req.UnitIds, ct);
        HttpContext.Response.StatusCode = StatusCodes.Status202Accepted;
    }
}

public sealed class ConfirmObjectOutputRequest : IActivityLoggable
{
    public Guid Id { get; set; }
    public string ArtifactHash { get; set; } = "";
    public List<string> ProducedUnitIds { get; set; } = [];
    public string EntityType => "PrintJob";
    public string EventType => "PrintJobObjectOutputConfirmed";
}
public sealed class ConfirmObjectOutputValidator : Validator<ConfirmObjectOutputRequest>
{
    public ConfirmObjectOutputValidator() { RuleFor(r => r.ArtifactHash).NotEmpty(); RuleFor(r => r.ProducedUnitIds).NotNull().Must(ids => ids is { Count: <= 10000 }); }
}
[Permission("print-jobs:remote-control")]
public sealed class ConfirmObjectOutputEndpoint(PrintObjectControlService objects) : Endpoint<ConfirmObjectOutputRequest, PrintObjectExecution>
{
    public override void Configure() { Post("/api/v1/print-jobs/{id}/object-output"); Policies("Permission"); }
    public override async Task HandleAsync(ConfirmObjectOutputRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        Response = await objects.ReconcileAsync(req.Id, req.ArtifactHash, req.ProducedUnitIds, ct);
    }
}

public sealed class ConfirmPrintNotStartedRequest : IActivityLoggable
{
    public Guid Id { get; set; }
    public string ArtifactHash { get; set; } = "";
    public string EntityType => "PrintJob";
    public string EventType => "PrintJobNotStartedConfirmed";
}
public sealed class ConfirmPrintNotStartedValidator : Validator<ConfirmPrintNotStartedRequest>
{
    public ConfirmPrintNotStartedValidator() => RuleFor(r => r.ArtifactHash).NotEmpty();
}
[Permission("print-jobs:remote-control")]
public sealed class ConfirmPrintNotStartedEndpoint(PrintObjectControlService objects) : Endpoint<ConfirmPrintNotStartedRequest, PrintObjectExecution>
{
    public override void Configure() { Post("/api/v1/print-jobs/{id}/confirm-not-started"); Policies("Permission"); }
    public override async Task HandleAsync(ConfirmPrintNotStartedRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        Response = await objects.ConfirmNotStartedAsync(req.Id, req.ArtifactHash, ct);
    }
}
