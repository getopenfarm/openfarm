using OpenFarm.Application.Common;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

// Re-parses the re-uploaded file with the same IProfileBundleParser used at preview and persists
// only the presets whose keys appear in the selection list (research.md §4) — every typed field and
// every byte of RawDataJson written to the database comes from this fresh parse, never from
// anything the client claims. Base/override resolution (Pass 2, research.md §3) is added in US2.
public sealed class ConfirmProfileImportCommandHandler(
    IProfileBundleParser parser,
    IMachineProfileRepository machineProfileRepository,
    IFilamentProfileRepository filamentProfileRepository)
    : ICommandHandler<ConfirmProfileImportCommand, ConfirmProfileImportResult>
{
    public async Task<ConfirmProfileImportResult> HandleAsync(ConfirmProfileImportCommand command, CancellationToken cancellationToken)
    {
        var sourceFormat = ProfileBundleFormatResolver.Resolve(command.FileName);
        var bundle = await parser.ParseAsync(command.FileStream, sourceFormat, cancellationToken);

        var createdMachineProfiles = new List<MachineProfile>();
        foreach (var selection in command.Selections.Where(s => s.Type == ProfileType.Machine))
        {
            if (selection.DuplicateAction == ProfileDuplicateAction.Skip)
            {
                continue;
            }

            var match = bundle.MachinePresets.FirstOrDefault(p => p.Name == selection.Name)
                ?? throw new ProfileSelectionMismatchException(nameof(ProfileType.Machine), selection.Name);

            var profile = MachineProfile.Create(
                Organization.WellKnownId, match.Name, sourceFormat, match.BaseProfileName, match.RawDataJson);
            await machineProfileRepository.AddAsync(profile, cancellationToken);
            createdMachineProfiles.Add(profile);
        }

        var createdFilamentProfiles = new List<FilamentProfile>();
        foreach (var selection in command.Selections.Where(s => s.Type == ProfileType.Filament))
        {
            if (selection.DuplicateAction == ProfileDuplicateAction.Skip)
            {
                continue;
            }

            var match = bundle.FilamentPresets.FirstOrDefault(p => p.Name == selection.Name)
                ?? throw new ProfileSelectionMismatchException(nameof(ProfileType.Filament), selection.Name);

            var profile = FilamentProfile.Create(
                Organization.WellKnownId, match.Name, sourceFormat, match.BaseProfileName, match.RawDataJson);
            await filamentProfileRepository.AddAsync(profile, cancellationToken);
            createdFilamentProfiles.Add(profile);
        }

        return new ConfirmProfileImportResult(
            createdMachineProfiles.Select(p => new CreatedProfileSummary(p.Id, p.Name, p.BaseProfileId)).ToList(),
            createdFilamentProfiles.Select(p => new CreatedProfileSummary(p.Id, p.Name, p.BaseProfileId)).ToList());
    }
}
