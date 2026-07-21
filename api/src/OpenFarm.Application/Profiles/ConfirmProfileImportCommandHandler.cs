using OpenFarm.Application.Common;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

// Re-parses the re-uploaded file with the same IProfileBundleParser used at preview and persists
// only the presets whose keys appear in the selection list (research.md §4) — every typed field and
// every byte of RawDataJson written to the database comes from this fresh parse, never from
// anything the client claims. Pass 2 (below) resolves each inserted row's declared base name into
// BaseProfileId where unambiguously possible (research.md §3).
public sealed class ConfirmProfileImportCommandHandler(
    IProfileBundleParser parser,
    IMachineProfileRepository machineProfileRepository,
    IFilamentProfileRepository filamentProfileRepository,
    IProcessProfileRepository processProfileRepository)
    : ICommandHandler<ConfirmProfileImportCommand, ConfirmProfileImportResult>
{
    public async Task<ConfirmProfileImportResult> HandleAsync(ConfirmProfileImportCommand command, CancellationToken cancellationToken)
    {
        var sourceFormat = ProfileBundleFormatResolver.Resolve(command.FileName);
        var bundle = await parser.ParseAsync(command.FileStream, sourceFormat, cancellationToken);

        // Pass 1 — insert and index. A Dictionary<string, Guid?> can't just map name -> Id: a
        // second insert under the same name must overwrite the first with an ambiguous sentinel
        // (null), never silently keep only one candidate (research.md §3) — FR-009 permits
        // duplicate-named presets as separate rows, so a same-batch name collision is a real,
        // expected input shape. Keyed by Name alone here (not (SourceFormat, Name)) since every
        // preset in one bundle already shares the same SourceFormat.
        var machineBatchIndex = new Dictionary<string, Guid?>(StringComparer.Ordinal);
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
            IndexBatchName(machineBatchIndex, match.Name, profile.Id);
        }

        var filamentBatchIndex = new Dictionary<string, Guid?>(StringComparer.Ordinal);
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
            IndexBatchName(filamentBatchIndex, match.Name, profile.Id);
        }

        var processBatchIndex = new Dictionary<string, Guid?>(StringComparer.Ordinal);
        var createdProcessProfiles = new List<ProcessProfile>();
        foreach (var selection in command.Selections.Where(s => s.Type == ProfileType.Process))
        {
            if (selection.DuplicateAction == ProfileDuplicateAction.Skip)
            {
                continue;
            }

            var match = bundle.ProcessPresets.FirstOrDefault(p => p.Name == selection.Name)
                ?? throw new ProfileSelectionMismatchException(nameof(ProfileType.Process), selection.Name);

            var profile = ProcessProfile.Create(
                Organization.WellKnownId, match.Name, sourceFormat, match.BaseProfileName, match.RawDataJson);
            await processProfileRepository.AddAsync(profile, cancellationToken);
            createdProcessProfiles.Add(profile);
            IndexBatchName(processBatchIndex, match.Name, profile.Id);
        }

        // Pass 2 — resolve. Every unresolved outcome (no match, or an ambiguous match from either
        // source) leaves BaseProfileId null while BaseProfileName stays stored verbatim — the same
        // "unresolved reference is a normal, expected outcome" behavior FR-005 already establishes.
        var machineNeedsSave = false;
        foreach (var profile in createdMachineProfiles)
        {
            if (profile.BaseProfileName is null)
            {
                continue;
            }

            var resolvedId = await ResolveBaseIdAsync(
                machineBatchIndex, profile.BaseProfileName,
                name => machineProfileRepository.FindByNameAsync(sourceFormat, name, cancellationToken));
            if (resolvedId is not null)
            {
                profile.ResolveBaseProfile(resolvedId.Value);
                machineNeedsSave = true;
            }
        }

        if (machineNeedsSave)
        {
            await machineProfileRepository.SaveChangesAsync(cancellationToken);
        }

        var filamentNeedsSave = false;
        foreach (var profile in createdFilamentProfiles)
        {
            if (profile.BaseProfileName is null)
            {
                continue;
            }

            var resolvedId = await ResolveBaseIdAsync(
                filamentBatchIndex, profile.BaseProfileName,
                name => filamentProfileRepository.FindByNameAsync(sourceFormat, name, cancellationToken));
            if (resolvedId is not null)
            {
                profile.ResolveBaseProfile(resolvedId.Value);
                filamentNeedsSave = true;
            }
        }

        if (filamentNeedsSave)
        {
            await filamentProfileRepository.SaveChangesAsync(cancellationToken);
        }

        var processNeedsSave = false;
        foreach (var profile in createdProcessProfiles)
        {
            if (profile.BaseProfileName is null)
            {
                continue;
            }

            var resolvedId = await ResolveBaseIdAsync(
                processBatchIndex, profile.BaseProfileName,
                name => processProfileRepository.FindByNameAsync(sourceFormat, name, cancellationToken));
            if (resolvedId is not null)
            {
                profile.ResolveBaseProfile(resolvedId.Value);
                processNeedsSave = true;
            }
        }

        if (processNeedsSave)
        {
            await processProfileRepository.SaveChangesAsync(cancellationToken);
        }

        return new ConfirmProfileImportResult(
            createdMachineProfiles.Select(p => new CreatedProfileSummary(p.Id, p.Name, p.BaseProfileId)).ToList(),
            createdFilamentProfiles.Select(p => new CreatedProfileSummary(p.Id, p.Name, p.BaseProfileId)).ToList(),
            createdProcessProfiles.Select(p => new CreatedProfileSummary(p.Id, p.Name, p.BaseProfileId)).ToList());
    }

    private static void IndexBatchName(Dictionary<string, Guid?> index, string name, Guid id)
    {
        // Second (or later) insert under the same name overwrites with the ambiguous sentinel and
        // stays that way for every subsequent insert of the same key.
        index[name] = index.ContainsKey(name) ? null : id;
    }

    // The sourceFormat scoping that matters for the prior-import DB lookup (a Bambu Studio preset
    // can never legitimately resolve against an OrcaSlicer-named row, research.md §3) is already
    // baked into the findByName delegate the caller supplies; the in-batch map needs no separate
    // scoping since one bundle has one SourceFormat throughout.
    private static async Task<Guid?> ResolveBaseIdAsync<TRow>(
        Dictionary<string, Guid?> batchIndex,
        string baseProfileName,
        Func<string, Task<IReadOnlyList<TRow>>> findByName)
        where TRow : class
    {
        if (batchIndex.TryGetValue(baseProfileName, out var batchMatch))
        {
            // Present (resolved or ambiguous-null) — stop, never fall through to a database lookup.
            return batchMatch;
        }

        var catalogMatches = await findByName(baseProfileName);
        if (catalogMatches.Count != 1)
        {
            return null;
        }

        return catalogMatches[0] switch
        {
            MachineProfile machineProfile => machineProfile.Id,
            FilamentProfile filamentProfile => filamentProfile.Id,
            ProcessProfile processProfile => processProfile.Id,
            _ => null,
        };
    }
}
