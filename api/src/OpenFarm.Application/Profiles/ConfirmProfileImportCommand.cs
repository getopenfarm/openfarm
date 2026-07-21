using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

public enum ProfileType
{
    Machine,
    Filament,
    Process,
}

public enum ProfileDuplicateAction
{
    Skip,
    ImportAsNew,
}

// Deliberately minimal (data-model.md's SelectedProfileKey): confirm re-uploads and re-parses the
// source file itself (research.md §4), so the only things that cross the wire are which presets to
// keep and the one decision the server can't re-derive — the duplicate resolution choice.
public sealed record SelectedProfileKey(ProfileType Type, string Name, ProfileDuplicateAction? DuplicateAction);

public sealed record ConfirmProfileImportCommand(Stream FileStream, string FileName, IReadOnlyList<SelectedProfileKey> Selections);

public sealed record CreatedProfileSummary(Guid Id, string Name, Guid? BaseProfileId);

public sealed record ConfirmProfileImportResult(
    IReadOnlyList<CreatedProfileSummary> CreatedMachineProfiles,
    IReadOnlyList<CreatedProfileSummary> CreatedFilamentProfiles,
    IReadOnlyList<CreatedProfileSummary> CreatedProcessProfiles);
