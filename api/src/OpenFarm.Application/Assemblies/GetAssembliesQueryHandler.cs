using OpenFarm.Application.Common;

namespace OpenFarm.Application.Assemblies;

public sealed class GetAssembliesQueryHandler(IAssemblyRepository assemblyRepository) : IQueryHandler<GetAssembliesQuery, AssembliesPage>
{
    public async Task<AssembliesPage> HandleAsync(GetAssembliesQuery query, CancellationToken cancellationToken)
    {
        var (assemblies, totalCount) = await assemblyRepository.ListInFolderAsync(query.Page, query.PageSize, query.Sorting, query.Folder, cancellationToken);

        var summaries = assemblies
            .Select(a => new AssemblySummary(a.Id, a.Name, a.Description, a.Lines.Count))
            .ToList();

        return new AssembliesPage(summaries, totalCount);
    }
}
