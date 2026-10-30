namespace OpenFarm.Domain.Library;

public sealed class LibraryFolder
{
    private LibraryFolder() { }
    public Guid Id { get; private set; }
    public string Library { get; private set; } = string.Empty;
    public Guid? ParentId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static LibraryFolder Create(string library, string name, Guid? parentId)
    {
        ValidateLibrary(library);
        var folder = new LibraryFolder { Id = Guid.NewGuid(), Library = library, ParentId = parentId, CreatedAtUtc = DateTimeOffset.UtcNow };
        folder.Rename(name);
        return folder;
    }
    public void Rename(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        name = name.Trim();
        if (name.Length is 0 or > 255) throw new ArgumentException("Folder name must contain 1–255 characters.", nameof(name));
        Name = name;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
    public void Move(Guid? parentId)
    {
        if (parentId == Id) throw new LibraryConflictException("A folder cannot contain itself.");
        ParentId = parentId;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
    public static void ValidateLibrary(string library)
    {
        if (library is not ("parts" or "assemblies")) throw new ArgumentException("Library must be parts or assemblies.", nameof(library));
    }
    // null preserves the legacy all-items query; root explicitly selects unfiled items.
    public static Guid? ParseLocation(string? location) => location is null or "root" ? null
        : Guid.TryParse(location, out var id) ? id : throw new ArgumentException("Folder must be root or a folder ID.", nameof(location));
}

public sealed class LibraryFolderNotFoundException() : Exception("Folder not found.");
public sealed class LibraryItemNotFoundException() : Exception("One or more items no longer exist. Refresh and try again.");
public sealed class LibraryConflictException(string message) : Exception(message);
