namespace OpenFarm.Application.Profiles;

// research.md §4's consistency check — a selected (type, name) key has no match in the freshly
// re-parsed bundle (most often the operator uploaded a different file than the one previewed).
// Mapped to 422 by DomainExceptionHandler.
public sealed class ProfileSelectionMismatchException(string type, string name)
    : Exception($"No {type} preset named '{name}' was found in the re-parsed bundle. Re-run preview and try again.")
{
    public string Type { get; } = type;

    public string Name { get; } = name;
}
