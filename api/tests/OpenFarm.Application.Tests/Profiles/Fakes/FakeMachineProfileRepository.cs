using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Profiles.Fakes;

public sealed class FakeMachineProfileRepository : IMachineProfileRepository
{
    private readonly List<MachineProfile> _rows = [];

    public IReadOnlyList<MachineProfile> Rows => _rows;

    public Task AddAsync(MachineProfile profile, CancellationToken cancellationToken)
    {
        _rows.Add(profile);
        return Task.CompletedTask;
    }

    public Task<MachineProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<MachineProfile>> ListAsync(CancellationToken cancellationToken, ProfileCatalogScope? scope = null) =>
        Task.FromResult<IReadOnlyList<MachineProfile>>(
            scope is null ? _rows.ToList() : _rows.Where(p => scope.Includes(p.Origin, p.SourceFormat, p.SourceVersion)).ToList());

    public Task<IReadOnlyList<MachineProfile>> FindByNameInCatalogAsync(
        ProfileSourceFormat sourceFormat, string? sourceVersion, string name, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MachineProfile>>(
            _rows.Where(p => p.SourceFormat == sourceFormat && p.Name == name
                && (p.Origin == ProfileOrigin.User || p.SourceVersion == sourceVersion)).ToList());

    public Task<IReadOnlyList<MachineProfile>> FindByNameAnyVersionAsync(
        ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MachineProfile>>(
            _rows.Where(p => p.SourceFormat == sourceFormat && p.Name == name).ToList());

    public Task<IReadOnlyList<MachineProfile>> FindInCatalogByIdentityAsync(
        ProfileSourceFormat sourceFormat, string sourceVersion, string? externalId, string? vendorName, string name, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MachineProfile>>(
            _rows.Where(p => p.SourceFormat == sourceFormat && p.Origin == ProfileOrigin.System && p.SourceVersion == sourceVersion
                && ((externalId != null && p.ExternalId == externalId) || (p.VendorName == vendorName && p.Name == name))).ToList());

    public Task<IReadOnlyList<MachineProfile>> ListSystemCatalogAsync(ProfileSourceFormat sourceFormat, string? sourceVersion, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MachineProfile>>(
            _rows.Where(p => p.SourceFormat == sourceFormat && p.Origin == ProfileOrigin.System && p.SourceVersion == sourceVersion).ToList());

    public void Stage(MachineProfile profile) => _rows.Add(profile);

    public Task<(IReadOnlyList<MachineProfile> Items, int TotalCount)> SearchAsync(
            ProfileOrigin? origin,
            bool? isSelectable,
            string? compatibleWithMachineName,
            string? materialType,
            string? searchText,
            int page,
            int pageSize,
            CancellationToken cancellationToken, OpenFarm.Application.Common.ListSort? sorting = null, ProfileCatalogScope? catalog = null)
    {
        var candidates = _rows.AsEnumerable();

        if (catalog is not null)
        {
            candidates = candidates.Where(p => catalog.Includes(p.Origin, p.SourceFormat, p.SourceVersion));
        }

        if (origin is { } requestedOrigin)
        {
            candidates = candidates.Where(p => p.Origin == requestedOrigin);
        }

        candidates = isSelectable switch
        {
            true => candidates.Where(p => (p.IsSelectable && p.IsCurrentlyOffered) || p.Origin == ProfileOrigin.User),
            false => candidates.Where(p => !((p.IsSelectable && p.IsCurrentlyOffered) || p.Origin == ProfileOrigin.User)),
            null => candidates,
        };

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            candidates = candidates.Where(p => p.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = (sorting ?? new OpenFarm.Application.Common.ListSort("name", false)).Order(candidates, p => p.Name, p => p.Id).ToList();
        var totalCount = ordered.Count;
        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Task.FromResult<(IReadOnlyList<MachineProfile> Items, int TotalCount)>((items, totalCount));
    }

    public Task<IReadOnlyList<string>> ListIngestedVersionsAsync(ProfileSourceFormat sourceFormat, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(
            _rows.Where(p => p.SourceFormat == sourceFormat && p.Origin == ProfileOrigin.System && p.SourceVersion is not null)
                .Select(p => p.SourceVersion!).Distinct().ToList());

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
