using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.FilamentTracking;
using CreateVendorHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.CreateVendorCommand, OpenFarm.Domain.FilamentTracking.Vendor>;

namespace OpenFarm.Api.Endpoints.Vendors;

public sealed class CreateVendorRequest : IActivityLoggable
{
    public string Name { get; set; } = string.Empty;

    public string EntityType => "Vendor";

    public string EventType => "VendorCreated";
}

public sealed class CreateVendorRequestValidator : AbstractValidator<CreateVendorRequest>
{
    public CreateVendorRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
    }
}

/// <summary>Creates a reusable Vendor catalog entry (FR-017). 409 on a duplicate name.</summary>
[Permission("vendors:create")]
public sealed class CreateVendorEndpoint(CreateVendorHandler handler) : Endpoint<CreateVendorRequest, VendorResponse>
{
    public override void Configure()
    {
        Post("/api/v1/vendors");
        Policies("Permission");
    }

    public override async Task HandleAsync(CreateVendorRequest req, CancellationToken ct)
    {
        var vendor = await handler.HandleAsync(new CreateVendorCommand(req.Name), ct);

        Response = VendorResponse.FromDomain(vendor);
        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
    }
}
