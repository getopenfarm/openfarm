using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Library;

namespace OpenFarm.Application.Library;

public sealed record LibraryContentsEntry(Guid Id, string Name, string Kind, LibraryFolder? Folder = null, PartSummary? Part = null, AssemblySummary? Assembly = null);
public sealed record LibraryContentsPage(IReadOnlyList<LibraryContentsEntry> Items, int TotalCount, IReadOnlyList<LibraryFolder> Ancestors);
public interface ILibraryContentsReader
{
    Task<LibraryContentsPage> ReadAsync(string library, Guid? parentId, int page, int pageSize, ListSort? sorting, CancellationToken ct);
}
