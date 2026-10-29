using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Security;
using OpenFarm.Application.Settings;
using OpenFarm.Domain.Settings;

namespace OpenFarm.Api.Endpoints.Settings;

public sealed class FeeDto
{
    public string Name { get; set; } = string.Empty;
    /// <summary><c>Percent</c> of the sale price, or <c>Fixed</c> per order.</summary>
    public string Type { get; set; } = "Percent";
    public decimal Amount { get; set; }
}

public sealed class FeeProfileDto
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<FeeDto> Fees { get; set; } = [];

    public static FeeProfileDto From(FeeProfile profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        Fees = profile.Fees.Select(f => new FeeDto { Name = f.Name, Type = f.Type, Amount = f.Amount }).ToList(),
    };
}

public sealed class FeesResponse
{
    public List<FeeProfileDto> Profiles { get; set; } = [];
}

public sealed class UpdateFeesRequest
{
    public List<FeeProfileDto> Profiles { get; set; } = [];
}

public sealed class UpdateFeesValidator : AbstractValidator<UpdateFeesRequest>
{
    public UpdateFeesValidator()
    {
        RuleForEach(x => x.Profiles).ChildRules(profile =>
        {
            profile.RuleFor(p => p.Name).NotEmpty().Must(n => n.Trim().Length <= 100).WithMessage("Name must be 100 characters or fewer.");
            profile.RuleForEach(p => p.Fees).ChildRules(fee =>
            {
                fee.RuleFor(f => f.Name).NotEmpty();
                fee.RuleFor(f => f.Type).Must(t => Fee.Types.Contains(t)).WithMessage("type must be Percent or Fixed.");
                fee.RuleFor(f => f.Amount).GreaterThanOrEqualTo(0m);
                fee.RuleFor(f => f.Amount).LessThan(100m).When(f => f.Type == "Percent");
            });
            profile.RuleFor(p => p.Fees)
                .Must(fees => fees.Where(f => f.Type == "Percent").Sum(f => f.Amount) < 100m)
                .WithMessage("Percent fees must total below 100%.");
        });
        RuleFor(x => x.Profiles)
            .Must(p => p.Where(x => x.Id.HasValue).Select(x => x.Id).Distinct().Count() == p.Count(x => x.Id.HasValue))
            .WithMessage("Fee profile IDs must be unique.");
        RuleFor(x => x.Profiles)
            .Must(p => p.Select(x => x.Name.Trim().ToUpperInvariant()).Distinct().Count() == p.Count)
            .WithMessage("Fee profile names must be unique.");
    }
}

[Permission("settings:fees:read")]
public sealed class GetFeesEndpoint(IFeeProfileRepository profiles) : EndpointWithoutRequest<FeesResponse>
{
    public override void Configure() { Get("/api/v1/settings/fees"); Policies("Permission"); }
    public override async Task HandleAsync(CancellationToken ct) =>
        Response = new() { Profiles = (await profiles.ListAsync(ct)).Select(FeeProfileDto.From).ToList() };
}

/// <summary>PUT /api/v1/settings/fees — replaces every fee profile.</summary>
[Permission("settings:fees:update")]
public sealed class UpdateFeesEndpoint(IFeeProfileRepository profiles) : Endpoint<UpdateFeesRequest, FeesResponse>
{
    public override void Configure() { Put("/api/v1/settings/fees"); Policies("Permission"); }
    public override async Task HandleAsync(UpdateFeesRequest req, CancellationToken ct)
    {
        var existingIds = (await profiles.ListAsync(ct)).Select(p => p.Id).ToHashSet();
        await profiles.ReplaceAllAsync(req.Profiles
            .Select(p => FeeProfile.Create(p.Name, p.Fees.Select(f => new Fee(f.Name, f.Type, f.Amount)).ToList(),
                p.Id is { } id && existingIds.Contains(id) ? id : null))
            .ToList(), ct);
        Response = new() { Profiles = (await profiles.ListAsync(ct)).Select(FeeProfileDto.From).ToList() };
    }
}

public sealed class SaveFeeProfileRequest
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<FeeDto> Fees { get; set; } = [];
}

public sealed class SaveFeeProfileValidator : AbstractValidator<SaveFeeProfileRequest>
{
    public SaveFeeProfileValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Must(n => n.Trim().Length <= 100).WithMessage("Name must be 100 characters or fewer.");
        RuleForEach(x => x.Fees).ChildRules(fee =>
        {
            fee.RuleFor(f => f.Name).NotEmpty();
            fee.RuleFor(f => f.Type).Must(t => Fee.Types.Contains(t)).WithMessage("type must be Percent or Fixed.");
            fee.RuleFor(f => f.Amount).GreaterThanOrEqualTo(0m);
            fee.RuleFor(f => f.Amount).LessThan(100m).When(f => f.Type == "Percent");
        });
        RuleFor(x => x.Fees).Must(fees => fees.Where(f => f.Type == "Percent").Sum(f => f.Amount) < 100m)
            .WithMessage("Percent fees must total below 100%.");
    }
}

[Permission("settings:fees:update")]
public sealed class CreateFeeProfileEndpoint(IFeeProfileRepository profiles) : Endpoint<SaveFeeProfileRequest, FeeProfileDto>
{
    public override void Configure()
    {
        Post("/api/v1/settings/fees/profiles");
        Policies("Permission");
        Description(b => b.Accepts<SaveFeeProfileRequest>("application/json")
            .Produces<FeeProfileDto>(201).Produces<ErrorResponse>(400).Produces(401).Produces(403), clearDefaults: true);
    }
    public override async Task HandleAsync(SaveFeeProfileRequest req, CancellationToken ct)
    {
        var result = await profiles.SaveAsync(null, req.Name, req.Fees.Select(f => new Fee(f.Name, f.Type, f.Amount)).ToList(), ct);
        if (result.DuplicateName)
        {
            AddError(r => r.Name, "Fee profile names must be unique.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }
        Response = FeeProfileDto.From(result.Profile!);
        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
    }
}

[Permission("settings:fees:update")]
public sealed class EditFeeProfileEndpoint(IFeeProfileRepository profiles) : Endpoint<SaveFeeProfileRequest, FeeProfileDto>
{
    public override void Configure()
    {
        Put("/api/v1/settings/fees/profiles/{id}");
        Policies("Permission");
        Description(b => b.Produces<ErrorResponse>(400).Produces(404));
    }
    public override async Task HandleAsync(SaveFeeProfileRequest req, CancellationToken ct)
    {
        var result = await profiles.SaveAsync(Route<Guid>("id"), req.Name, req.Fees.Select(f => new Fee(f.Name, f.Type, f.Amount)).ToList(), ct);
        if (result.DuplicateName)
        {
            AddError(r => r.Name, "Fee profile names must be unique.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }
        if (result.Profile is null) { await Send.NotFoundAsync(ct); return; }
        Response = FeeProfileDto.From(result.Profile);
    }
}

public sealed class RemoveFeeProfileRequest { public Guid Id { get; set; } }

[Permission("settings:fees:update")]
public sealed class RemoveFeeProfileEndpoint(IFeeProfileRepository profiles) : Endpoint<RemoveFeeProfileRequest>
{
    public override void Configure()
    {
        Delete("/api/v1/settings/fees/profiles/{id}");
        Policies("Permission");
        Description(b => b.Produces(204).Produces(401).Produces(403).Produces(404), clearDefaults: true);
    }
    public override async Task HandleAsync(RemoveFeeProfileRequest req, CancellationToken ct)
    {
        if (!await profiles.RemoveAsync(Route<Guid>("id"), ct)) { await Send.NotFoundAsync(ct); return; }
        await HttpContext.Response.SendNoContentAsync(ct);
    }
}
