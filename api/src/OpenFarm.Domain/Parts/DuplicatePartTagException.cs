namespace OpenFarm.Domain.Parts;

public sealed class DuplicatePartTagException(string tagName)
    : Exception($"Tag '{tagName}' is already associated with this part.")
{
    public string TagName { get; } = tagName;
}
