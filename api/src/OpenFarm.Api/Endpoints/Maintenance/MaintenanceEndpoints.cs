using System.IdentityModel.Tokens.Jwt;
using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Maintenance;
using OpenFarm.Domain.Maintenance;
using EnrollmentsHandler = OpenFarm.Application.Common.IQueryHandler<OpenFarm.Application.Maintenance.ListMaintenanceEnrollmentsQuery, System.Collections.Generic.IReadOnlyList<OpenFarm.Application.Maintenance.MaintenanceEnrollmentView>>;
using LogsHandler = OpenFarm.Application.Common.IQueryHandler<OpenFarm.Application.Maintenance.ListMaintenanceLogsQuery, (System.Collections.Generic.IReadOnlyList<OpenFarm.Application.Maintenance.MaintenanceLogView> Items, int TotalCount)>;
using TasksHandler = OpenFarm.Application.Common.IQueryHandler<OpenFarm.Application.Maintenance.ListMaintenanceTasksQuery, System.Collections.Generic.IReadOnlyList<OpenFarm.Application.Maintenance.MaintenanceTaskView>>;

namespace OpenFarm.Api.Endpoints.Maintenance;

[Permission("maintenance:view")]
public sealed class ListMaintenanceTasksEndpoint(TasksHandler tasks) : EndpointWithoutRequest<MaintenanceTasksResponse>
{
    public override void Configure() { Get("/api/v1/maintenance/tasks"); Policies("Permission"); }
    public override async Task HandleAsync(CancellationToken ct) =>
        Response = new((await tasks.HandleAsync(new ListMaintenanceTasksQuery(), ct)).Select(MaintenanceTaskResponse.From).ToList());
}

public sealed class ListMaintenanceEnrollmentsRequest
{
    public Guid? PrinterId { get; set; }
    public Guid? TaskId { get; set; }

    /// <summary><c>attention</c> (due soon or overdue), <c>Ok</c>, <c>DueSoon</c> or <c>Overdue</c>.</summary>
    public string? State { get; set; }
}

/// <summary>Most due first.</summary>
[Permission("maintenance:view")]
public sealed class ListMaintenanceEnrollmentsEndpoint(EnrollmentsHandler enrollments)
    : Endpoint<ListMaintenanceEnrollmentsRequest, MaintenanceEnrollmentsResponse>
{
    public override void Configure() { Get("/api/v1/maintenance/enrollments"); Policies("Permission"); }
    public override async Task HandleAsync(ListMaintenanceEnrollmentsRequest req, CancellationToken ct) =>
        Response = MaintenanceEnrollmentsResponse.From(await enrollments.HandleAsync(new ListMaintenanceEnrollmentsQuery(req.PrinterId, req.TaskId, req.State), ct));
}

public sealed class ListMaintenanceLogsRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public Guid? PrinterId { get; set; }
    public Guid? TaskId { get; set; }
}

/// <summary>Newest first. Names are live, or the snapshot once the task or printer is gone.</summary>
[Permission("maintenance:view")]
public sealed class ListMaintenanceLogsEndpoint(LogsHandler logs) : Endpoint<ListMaintenanceLogsRequest, MaintenanceLogsResponse>
{
    public override void Configure() { Get("/api/v1/maintenance/logs"); Policies("Permission"); }
    public override async Task HandleAsync(ListMaintenanceLogsRequest req, CancellationToken ct)
    {
        var (items, total) = await logs.HandleAsync(
            new ListMaintenanceLogsQuery(Math.Max(1, req.Page), req.PageSize is < 1 or > 200 ? 25 : req.PageSize, req.PrinterId, req.TaskId), ct);
        Response = new(items, total);
    }
}

public sealed class SaveMaintenanceTaskRequest : IActivityLoggable
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public decimal? EveryPrintHours { get; set; }
    public int? EveryDays { get; set; }
    public int? EveryPrints { get; set; }
    public int WarnAtPercent { get; set; } = MaintenanceTask.DefaultWarnAtPercent;

    /// <summary>Create only: printers to enroll, starting from <see cref="LastDoneAtUtc"/> (default now) plus the carries.</summary>
    public List<Guid> PrinterIds { get; set; } = [];
    public DateTimeOffset? LastDoneAtUtc { get; set; }
    public decimal CarryHours { get; set; }
    public int CarryPrints { get; set; }

    public string EntityType => "MaintenanceTask";
    public string EventType => Id == Guid.Empty ? "MaintenanceTaskCreated" : "MaintenanceTaskEdited";

    internal MaintenanceDefinition Definition => new(Name, Notes, EveryPrintHours, EveryDays, EveryPrints, WarnAtPercent);
}

public sealed class SaveMaintenanceTaskRequestValidator : AbstractValidator<SaveMaintenanceTaskRequest>
{
    public SaveMaintenanceTaskRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(MaintenanceTask.MaxNameLength);
        RuleFor(x => x).Must(x => x.EveryPrintHours is not null || x.EveryDays is not null || x.EveryPrints is not null)
            .WithMessage("Set at least one interval.");
        RuleFor(x => x.EveryPrintHours).GreaterThan(0);
        RuleFor(x => x.EveryDays).GreaterThan(0);
        RuleFor(x => x.EveryPrints).GreaterThan(0);
        RuleFor(x => x.WarnAtPercent).InclusiveBetween(1, 99);
        RuleFor(x => x.CarryHours).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CarryPrints).GreaterThanOrEqualTo(0);
    }
}

[Permission("maintenance:manage")]
public sealed class CreateMaintenanceTaskEndpoint(MaintenanceCommandHandlers commands, TasksHandler tasks)
    : Endpoint<SaveMaintenanceTaskRequest, MaintenanceTaskResponse>
{
    public override void Configure() { Post("/api/v1/maintenance/tasks"); Policies("Permission"); }
    public override async Task HandleAsync(SaveMaintenanceTaskRequest req, CancellationToken ct)
    {
        req.Id = Guid.Empty;
        var id = await commands.HandleAsync(new CreateMaintenanceTaskCommand(
            req.Definition, req.PrinterIds, new MaintenanceBaseline(req.LastDoneAtUtc, req.CarryHours, req.CarryPrints)), ct);
        Response = MaintenanceTaskResponse.From((await tasks.HandleAsync(new ListMaintenanceTasksQuery(id), ct)).Single());
        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
    }
}

/// <summary>Changes the definition for every enrolled printer. Enrollment fields are ignored; use the enrollment endpoints.</summary>
[Permission("maintenance:manage")]
public sealed class EditMaintenanceTaskEndpoint(MaintenanceCommandHandlers commands, TasksHandler tasks)
    : Endpoint<SaveMaintenanceTaskRequest, MaintenanceTaskResponse>
{
    public override void Configure() { Put("/api/v1/maintenance/tasks/{id}"); Policies("Permission"); }
    public override async Task HandleAsync(SaveMaintenanceTaskRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        await commands.HandleAsync(new UpdateMaintenanceTaskCommand(req.Id, req.Definition), ct);
        Response = MaintenanceTaskResponse.From((await tasks.HandleAsync(new ListMaintenanceTasksQuery(req.Id), ct)).Single());
    }
}

public sealed class DeleteMaintenanceTaskRequest : IActivityLoggable
{
    public Guid Id { get; set; }
    public string EntityType => "MaintenanceTask";
    public string EventType => "MaintenanceTaskDeleted";
}

/// <summary>Removes the task and its enrollments; its logs stay.</summary>
[Permission("maintenance:delete")]
public sealed class DeleteMaintenanceTaskEndpoint(MaintenanceCommandHandlers commands) : Endpoint<DeleteMaintenanceTaskRequest>
{
    public override void Configure() { Delete("/api/v1/maintenance/tasks/{id}"); Policies("Permission"); }
    public override async Task HandleAsync(DeleteMaintenanceTaskRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        await commands.HandleAsync(new DeleteMaintenanceTaskCommand(req.Id), ct);
        await HttpContext.Response.SendNoContentAsync(ct);
    }
}

public sealed class EnrollPrintersRequest : IActivityLoggable
{
    public List<Guid> PrinterIds { get; set; } = [];
    public DateTimeOffset? LastDoneAtUtc { get; set; }
    public decimal CarryHours { get; set; }
    public int CarryPrints { get; set; }
    public string EntityType => "Printer";
    public string EventType => "MaintenanceEnrolled";
}

public sealed class EnrollPrintersRequestValidator : AbstractValidator<EnrollPrintersRequest>
{
    public EnrollPrintersRequestValidator()
    {
        RuleFor(x => x.PrinterIds).NotEmpty();
        RuleFor(x => x.CarryHours).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CarryPrints).GreaterThanOrEqualTo(0);
    }
}

/// <summary>Printers already enrolled are skipped; the response lists the newly enrolled ones.</summary>
[Permission("maintenance:manage")]
public sealed class EnrollPrintersEndpoint(MaintenanceCommandHandlers commands) : Endpoint<EnrollPrintersRequest, MaintenanceChangeResponse>
{
    public override void Configure() { Post("/api/v1/maintenance/tasks/{id}/enrollments"); Policies("Permission"); }
    public override async Task HandleAsync(EnrollPrintersRequest req, CancellationToken ct) =>
        Response = MaintenanceChangeResponse.ForPrinters(await commands.HandleAsync(new EnrollPrintersCommand(
            Route<Guid>("id"), req.PrinterIds, new MaintenanceBaseline(req.LastDoneAtUtc, req.CarryHours, req.CarryPrints)), ct));
}

public sealed class UnenrollPrinterRequest : IActivityLoggable
{
    public Guid TaskId { get; set; }
    public Guid PrinterId { get; set; }

    /// <summary>The activity-log entity: the printer.</summary>
    public Guid Id => PrinterId;
    public string EntityType => "Printer";
    public string EventType => "MaintenanceUnenrolled";
}

[Permission("maintenance:manage")]
public sealed class UnenrollPrinterEndpoint(MaintenanceCommandHandlers commands) : Endpoint<UnenrollPrinterRequest>
{
    public override void Configure() { Delete("/api/v1/maintenance/tasks/{taskId}/enrollments/{printerId}"); Policies("Permission"); }
    public override async Task HandleAsync(UnenrollPrinterRequest req, CancellationToken ct)
    {
        req.TaskId = Route<Guid>("taskId");
        req.PrinterId = Route<Guid>("printerId");
        await commands.HandleAsync(new UnenrollPrinterCommand(req.TaskId, req.PrinterId), ct);
        await HttpContext.Response.SendNoContentAsync(ct);
    }
}

public sealed class EnrollPrinterInTasksRequest : IActivityLoggable
{
    public Guid Id { get; set; }
    public List<Guid> TaskIds { get; set; } = [];
    public string EntityType => "Printer";
    public string EventType => "MaintenanceEnrolled";
}

public sealed class EnrollPrinterInTasksRequestValidator : AbstractValidator<EnrollPrinterInTasksRequest>
{
    public EnrollPrinterInTasksRequestValidator() => RuleFor(x => x.TaskIds).NotEmpty();
}

/// <summary>"Add tasks": enrolls the printer in existing tasks from now. Tasks it is already in are skipped; never creates tasks.</summary>
[Permission("maintenance:manage")]
public sealed class EnrollPrinterInTasksEndpoint(MaintenanceCommandHandlers commands)
    : Endpoint<EnrollPrinterInTasksRequest, MaintenanceChangeResponse>
{
    public override void Configure() { Post("/api/v1/printers/{id}/maintenance/enrollments"); Policies("Permission"); }
    public override async Task HandleAsync(EnrollPrinterInTasksRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        var added = await commands.HandleAsync(new EnrollPrinterInTasksCommand(req.Id, req.TaskIds), ct);
        Response = new() { EntityIds = added.Count > 0 ? [req.Id.ToString()] : [], Count = added.Count };
    }
}

public sealed class RebaselineMaintenanceRequest : IActivityLoggable
{
    public DateTimeOffset LastDoneAtUtc { get; set; }
    public decimal CarryHours { get; set; }
    public int CarryPrints { get; set; }
    public string EntityType => "Printer";
    public string EventType => "MaintenanceRebaselined";
}

public sealed class RebaselineMaintenanceRequestValidator : AbstractValidator<RebaselineMaintenanceRequest>
{
    public RebaselineMaintenanceRequestValidator()
    {
        RuleFor(x => x.LastDoneAtUtc).NotEmpty();
        RuleFor(x => x.CarryHours).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CarryPrints).GreaterThanOrEqualTo(0);
    }
}

/// <summary>"Set last done": restarts the cycle at <c>lastDoneAtUtc</c> with usage from before tracking.</summary>
[Permission("maintenance:manage")]
public sealed class RebaselineMaintenanceEndpoint(MaintenanceCommandHandlers commands)
    : Endpoint<RebaselineMaintenanceRequest, MaintenanceChangeResponse>
{
    public override void Configure() { Put("/api/v1/maintenance/enrollments/{id}/baseline"); Policies("Permission"); }
    public override async Task HandleAsync(RebaselineMaintenanceRequest req, CancellationToken ct) =>
        Response = MaintenanceChangeResponse.ForPrinters([await commands.HandleAsync(new RebaselineMaintenanceCommand(
            Route<Guid>("id"), new MaintenanceBaseline(req.LastDoneAtUtc, req.CarryHours, req.CarryPrints)), ct)]);
}

public sealed class CompleteMaintenanceRequest : IActivityLoggable
{
    public List<Guid> EnrollmentIds { get; set; } = [];

    /// <summary>Default now; may be backdated, but not before the cycle start.</summary>
    public DateTimeOffset? PerformedAtUtc { get; set; }
    public string? Notes { get; set; }

    /// <summary>Free text on top of the signed-in user, e.g. who did the work.</summary>
    public string? PerformedByNote { get; set; }
    public decimal? Cost { get; set; }
    public string EntityType => "Printer";
    public string EventType => "MaintenanceCompleted";
}

public sealed class CompleteMaintenanceRequestValidator : AbstractValidator<CompleteMaintenanceRequest>
{
    public CompleteMaintenanceRequestValidator()
    {
        RuleFor(x => x.EnrollmentIds).NotEmpty();
        RuleFor(x => x.Cost).GreaterThanOrEqualTo(0);
    }
}

/// <summary>Mark done, all or none: any failure is a 422 whose <c>failures</c> maps each failing enrollment id to why.</summary>
[Permission("maintenance:complete")]
public sealed class CompleteMaintenanceEndpoint(MaintenanceCommandHandlers commands) : Endpoint<CompleteMaintenanceRequest, MaintenanceChangeResponse>
{
    public override void Configure() { Post("/api/v1/maintenance/enrollments/complete"); Policies("Permission"); }
    public override async Task HandleAsync(CompleteMaintenanceRequest req, CancellationToken ct)
    {
        _ = Guid.TryParse(HttpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId);
        Response = MaintenanceChangeResponse.ForPrinters(await commands.HandleAsync(new CompleteMaintenanceCommand(
            req.EnrollmentIds, req.PerformedAtUtc, userId, req.PerformedByNote, req.Notes, req.Cost), ct));
    }
}
