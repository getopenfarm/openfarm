using FluentAssertions;
using OpenFarm.Application.Auth;

namespace OpenFarm.Application.Tests.Auth;

public class PermissionCatalogTests
{
    [Fact]
    public void EveryArea_HasAtLeastOnePermission_AndEveryPermissionAKnownArea()
    {
        var areas = PermissionCatalog.Areas.Select(a => a.Key).ToArray();
        areas.Should().OnlyHaveUniqueItems();
        foreach (var area in areas) PermissionCatalog.All.Should().Contain(p => p.Area == area, area);
        PermissionCatalog.All.Should().OnlyContain(p => areas.Contains(p.Area) && p.Key.StartsWith(p.Area + ":", StringComparison.Ordinal));
        PermissionCatalog.All.Select(p => p.Key).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Permissions_FollowAreaOrder_ThenReadsWriteDestructive()
    {
        var areaIndex = PermissionCatalog.Areas.Select((a, i) => (a.Key, i)).ToDictionary(x => x.Key, x => x.i);
        PermissionCatalog.All.Select(p => areaIndex[p.Area] * 10 + (p.IsRead ? 0 : p.IsDestructive ? 2 : 1)).Should().BeInAscendingOrder();
    }

    [Fact]
    public void WithinAnArea_LabelsAndVerbsAreUnique_AndEveryPermissionReadsInASentence()
    {
        foreach (var area in PermissionCatalog.All.GroupBy(p => p.Area))
        {
            area.Select(p => p.Label).Should().OnlyHaveUniqueItems(area.Key);
            area.Where(p => p.Verb is not null).Select(p => p.Verb).Should().OnlyHaveUniqueItems(area.Key);
        }
        PermissionCatalog.All.Should().OnlyContain(p => (p.Verb == null) != (p.Phrase == null) && p.Description.EndsWith('.'));
    }

    [Fact]
    public void AdministratorOnlyAreas_AreNotGrantable_AndSortFollowsTheCatalog()
    {
        PermissionCatalog.IsGrantable("users:manage").Should().BeFalse();
        PermissionCatalog.IsGrantable("printers:list").Should().BeTrue();
        PermissionCatalog.IsGrantable("unknown:key").Should().BeFalse();
        PermissionCatalog.Sort(["zzz:unknown", "tags:list", "printers:remove", "printers:list"])
            .Should().Equal("printers:list", "printers:remove", "tags:list", "zzz:unknown");
    }
}
