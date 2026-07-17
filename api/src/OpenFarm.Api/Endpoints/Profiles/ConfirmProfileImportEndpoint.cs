using System.Text.Json;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Profiles;
using ConfirmProfileImportHandler = OpenFarm.Application.Common.ICommandHandler<
    OpenFarm.Application.Profiles.ConfirmProfileImportCommand, OpenFarm.Application.Profiles.ConfirmProfileImportResult>;

namespace OpenFarm.Api.Endpoints.Profiles;

public sealed class ConfirmProfileImportRequest : IActivityLoggable
{
    public IFormFile File { get; set; } = null!;

    // JSON-encoded string form field (research.md §6) — FastEndpoints binds the file part natively;
    // the selection list travels as a plain string field rather than a nested-JSON multipart binding.
    public string Selections { get; set; } = string.Empty;

    public string EntityType => "Profile";

    public string EventType => "ProfileImported";
}

public sealed class CreatedProfileItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? BaseProfileId { get; set; }
}

// IHasEntityIds (research.md §7) — one HTTP call creates many entities, so ActivityLogPostProcessor
// writes one ActivityLogEntry per id here rather than its default single-Id-reflection path.
public sealed class ConfirmProfileImportResponse : IHasEntityIds
{
    public List<CreatedProfileItem> CreatedMachineProfiles { get; set; } = [];
    public List<CreatedProfileItem> CreatedFilamentProfiles { get; set; } = [];

    // Internal to the post-processor extension only (research.md §7) — not part of the public
    // response contract (contracts/profiles-api.md), so excluded from JSON serialization.
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> EntityIds =>
        CreatedMachineProfiles.Select(p => p.Id.ToString())
            .Concat(CreatedFilamentProfiles.Select(p => p.Id.ToString()))
            .ToList();
}

// POST /api/v1/profiles/imports/confirm — re-uploads the same file and re-parses it server-side
// rather than trusting client-echoed preset content (research.md §4's revision note); persists only
// the presets whose keys appear in Selections (FR-004).
[Permission("profiles:confirm-import")]
public sealed class ConfirmProfileImportEndpoint(ConfirmProfileImportHandler handler)
    : Endpoint<ConfirmProfileImportRequest, ConfirmProfileImportResponse>
{
    private const long MaxBundleSizeBytes = 20 * 1024 * 1024;

    private static readonly JsonSerializerOptions SelectionsJsonOptions = new() { PropertyNameCaseInsensitive = true };

    public override void Configure()
    {
        Post("/api/v1/profiles/imports/confirm");
        AllowFileUploads();
        MaxRequestBodySize(MaxBundleSizeBytes);
        Policies("Permission");
    }

    public override async Task HandleAsync(ConfirmProfileImportRequest req, CancellationToken ct)
    {
        var selections = DeserializeSelections(req.Selections);

        await using var stream = req.File.OpenReadStream();
        var command = new ConfirmProfileImportCommand(stream, req.File.FileName, selections);
        var result = await handler.HandleAsync(command, ct);

        Response = new ConfirmProfileImportResponse
        {
            CreatedMachineProfiles = result.CreatedMachineProfiles
                .Select(p => new CreatedProfileItem { Id = p.Id, Name = p.Name, BaseProfileId = p.BaseProfileId })
                .ToList(),
            CreatedFilamentProfiles = result.CreatedFilamentProfiles
                .Select(p => new CreatedProfileItem { Id = p.Id, Name = p.Name, BaseProfileId = p.BaseProfileId })
                .ToList(),
        };
        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
    }

    private static List<SelectedProfileKey> DeserializeSelections(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        var dtos = JsonSerializer.Deserialize<List<SelectionDto>>(json, SelectionsJsonOptions) ?? [];
        return dtos.Select(d => new SelectedProfileKey(
            Enum.Parse<ProfileType>(d.Type, ignoreCase: true),
            d.Name,
            string.IsNullOrEmpty(d.DuplicateAction) ? null : Enum.Parse<ProfileDuplicateAction>(d.DuplicateAction, ignoreCase: true)))
            .ToList();
    }

    private sealed class SelectionDto
    {
        public string Type { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? DuplicateAction { get; set; }
    }
}
