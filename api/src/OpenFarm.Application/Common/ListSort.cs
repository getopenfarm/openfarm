namespace OpenFarm.Application.Common;

/// <summary>A validated single-column ordering for paged resource lists.</summary>
public sealed record ListSort(string Field, bool Descending)
{
    public static ListSort? Parse(string? field, string? direction, string allowedField)
    {
        if (field is null && direction is null) return null;
        if (field != allowedField || direction is not (null or "asc" or "desc"))
            throw new InvalidListSortException($"Use sortBy={allowedField} with sortDirection=asc or desc.");
        return new(field, direction == "desc");
    }

    public IOrderedEnumerable<T> Order<T>(IEnumerable<T> rows, Func<T, string> name, Func<T, Guid> id) =>
        (Descending ? rows.OrderByDescending(name, StringComparer.OrdinalIgnoreCase)
            : rows.OrderBy(name, StringComparer.OrdinalIgnoreCase)).ThenBy(id);
}

public sealed class InvalidListSortException(string message) : Exception(message);
