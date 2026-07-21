namespace OpenFarm.Infrastructure.Parts;

public sealed class PartFileStorageOptions
{
    public const string SectionName = "PartFileStorage";

    /// <summary>Bind-mounted volume root (infra/docker-compose.yml's <c>openfarm_part_files</c>).</summary>
    public string RootDirectory { get; set; } = "/data/part-files";
}
