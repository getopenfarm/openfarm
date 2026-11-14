using FluentAssertions;
using OpenFarm.Application.Auth;

namespace OpenFarm.Application.Tests.Auth;

public sealed class OAuthScopesTests
{
    // Frozen. An app already granted a scope gains whatever it expands to, so widening one is a
    // security change: add a new scope instead, and add a line here.
    private static readonly Dictionary<string, (ScopeTier Tier, string[] Permissions)> Snapshot = new()
    {
        ["printers.read"] = (ScopeTier.Read, ["printers:list", "printers:get-detail"]),
        ["print-jobs.read"] = (ScopeTier.Read, ["print-jobs:view"]),
        ["production-runs.read"] = (ScopeTier.Read, ["production-runs:view"]),
        ["library.read"] = (ScopeTier.Read, ["parts:list", "parts:view", "parts:read", "assemblies:view", "library:view"]),
        ["filament.read"] = (ScopeTier.Read, ["filaments:list", "spools:list"]),
        ["maintenance.read"] = (ScopeTier.Read, ["maintenance:view"]),
        ["pricing.read"] = (ScopeTier.Read, ["settings:fees:read"]),
        ["production-runs.write"] = (ScopeTier.Change, ["production-runs:create"]),
        ["library.write"] = (ScopeTier.Change, ["library:manage", "parts:edit", "assemblies:create", "assemblies:edit"]),
        ["filament.write"] = (ScopeTier.Change, ["spools:register"]),
        ["maintenance.write"] = (ScopeTier.Change, ["maintenance:complete", "maintenance:manage"]),
        ["pricing.write"] = (ScopeTier.Change, ["settings:fees:update"]),
        ["printers.ready"] = (ScopeTier.PrinterControl, ["printers:mark-ready"]),
        ["print-jobs.cancel"] = (ScopeTier.PrinterControl, ["print-jobs:cancel"]),
    };

    [Fact]
    public void RetiredQueueScopesGrantNothing_WhileCancelRemainsAvailable()
    {
        string[] retired = ["print-jobs.write", "print-jobs.pause"];
        OAuthScopes.Expand(retired).Should().BeEmpty();
        OAuthScopes.Parse(string.Join(" ", retired)).Should().BeEmpty();
        foreach (var scope in retired)
            OAuthScopes.Find(scope).Should().BeNull();
        OAuthScopes.Expand(["print-jobs.cancel"]).Should().Equal("print-jobs:cancel");
    }

    [Fact]
    public void EveryExpansionIsFrozen()
    {
        OAuthScopes.All.Select(s => s.Scope).Should().Equal(Snapshot.Keys);
        foreach (var scope in OAuthScopes.All)
        {
            scope.Tier.Should().Be(Snapshot[scope.Scope].Tier, scope.Scope);
            scope.Permissions.Should().Equal(Snapshot[scope.Scope].Permissions, scope.Scope);
        }
    }

    [Fact]
    public void ScopesUseKnownReadKeysForReads_AndNeverAdministrativeOrRemovalKeys()
    {
        string[] banned = ["printers:register", "printers:edit-connection", "printers:verify-connection", "printers:remove", "activity-log:list"];
        string[] settings = ["settings:fees:read", "settings:fees:update"];
        foreach (var scope in OAuthScopes.All)
            foreach (var key in scope.Permissions)
            {
                PermissionCatalog.IsKnown(key).Should().BeTrue(key);
                key.Should().NotStartWith("users:").And.NotEndWith(":delete");
                if (key.StartsWith("settings:", StringComparison.Ordinal)) settings.Should().Contain(key, "fee profiles are the only setting an app may reach");
                banned.Should().NotContain(key);
                if (scope.Tier == ScopeTier.Read) PermissionCatalog.All.Single(p => p.Key == key).IsRead.Should().BeTrue(key);
            }
    }

    [Fact]
    public void EachPermissionBelongsToOneScope_SoPermissionGatingImpliesScopeGating()
    {
        OAuthScopes.All.SelectMany(s => s.Permissions).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ParseKeepsKnownScopesOnce_InCatalogOrder()
    {
        OAuthScopes.Parse("openid print-jobs.read  printers.read print-jobs.read profile").Should().Equal("printers.read", "print-jobs.read");
        OAuthScopes.Parse(null).Should().BeEmpty();
        OAuthScopes.Parse("PRINTERS.READ").Should().BeEmpty();
    }

    [Fact]
    public void ExpandAndGrantable()
    {
        OAuthScopes.Expand(["printers.read", "nope"]).Should().BeEquivalentTo("printers:list", "printers:get-detail");
        var held = new HashSet<string> { "parts:list" };
        OAuthScopes.Grantable("library.read", held).Should().BeTrue();
        OAuthScopes.Grantable("printers.read", held).Should().BeFalse();
        OAuthScopes.Grantable("nope", held).Should().BeFalse();
    }

    [Fact]
    public void IncludesNamesAreaAndPermissionLabels()
    {
        OAuthScopes.Includes(OAuthScopes.Find("printers.read")!).Should().Equal("Printers: List", "Printers: View");
    }

    [Fact]
    public void ChipsGroupByArea()
    {
        var chips = OAuthScopes.Chips(["print-jobs.cancel", "printers.read", "print-jobs.read"]);
        chips.Should().HaveCount(2);
        chips[0].Should().BeEquivalentTo(new AccessChip("Printers", "Read", [], "Read printers, their live status and feed units", false));
        chips[1].Area.Should().Be("Queue");
        chips[1].Level.Should().Be("Read");
        chips[1].Extras.Should().Equal("Cancel");
        chips[1].Control.Should().BeTrue();
        chips[1].Title.Should().Be("Read the queue and print jobs; Cancel jobs and stop running prints");
    }
}
