using FluentAssertions;
using OpenFarm.Domain.Library;

namespace OpenFarm.Domain.Tests.Library;

public class LibraryFolderTests
{
    [Fact]
    public void Create_TrimsNameAndKeepsStableIdentityWhenRenamedOrMoved()
    {
        var folder = LibraryFolder.Create("parts", "  Brackets  ", null);
        var id = folder.Id;
        folder.Name.Should().Be("Brackets");
        folder.Rename("Mounts");
        var parent = Guid.NewGuid();
        folder.Move(parent);
        folder.Id.Should().Be(id);
        folder.ParentId.Should().Be(parent);
    }
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsEmptyNames(string name)
    {
        var act = () => LibraryFolder.Create("parts", name, null);
        act.Should().Throw<ArgumentException>();
    }
    [Fact]
    public void Create_RejectsLongNamesAndUnknownLibraries()
    {
        var longName = () => LibraryFolder.Create("parts", new string('a', 256), null);
        var library = () => LibraryFolder.Create("jobs", "A", null);
        longName.Should().Throw<ArgumentException>();
        library.Should().Throw<ArgumentException>();
    }
    [Fact]
    public void Move_RejectsSelf()
    {
        var folder = LibraryFolder.Create("assemblies", "A", null);
        var act = () => folder.Move(folder.Id);
        act.Should().Throw<LibraryConflictException>();
    }
    [Fact]
    public void Location_DistinguishesRootAndMalformedIds()
    {
        LibraryFolder.ParseLocation("root").Should().BeNull();
        var id = Guid.NewGuid();
        LibraryFolder.ParseLocation(id.ToString()).Should().Be(id);
        var act = () => LibraryFolder.ParseLocation("invalid");
        act.Should().Throw<ArgumentException>();
    }
}
