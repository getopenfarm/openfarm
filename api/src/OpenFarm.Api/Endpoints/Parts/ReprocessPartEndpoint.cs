using System.IdentityModel.Tokens.Jwt;
using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Parts;
using Handler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Parts.ReprocessPartCommand, OpenFarm.Application.Parts.AddPartResult>;

namespace OpenFarm.Api.Endpoints.Parts;

[Permission("parts:replace-file")]
public sealed class ReprocessPartEndpoint(Handler handler) : EndpointWithoutRequest<PartProcessingJobSubmittedResponse>
{
    public override void Configure()
    {
        Post("/api/v1/parts/{id}/reprocess");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        _ = Guid.TryParse(HttpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId);
        var result = await handler.HandleAsync(new ReprocessPartCommand(Route<Guid>("id"), userId), ct);
        Response = new PartProcessingJobSubmittedResponse { JobId = result.JobId, Status = result.Status.ToString() };
        HttpContext.Response.StatusCode = StatusCodes.Status202Accepted;
    }
}
