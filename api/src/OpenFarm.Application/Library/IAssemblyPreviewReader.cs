namespace OpenFarm.Application.Library;

public interface IAssemblyPreviewReader
{
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> ReadAsync(IReadOnlyList<Guid> assemblyIds, CancellationToken ct);
}
