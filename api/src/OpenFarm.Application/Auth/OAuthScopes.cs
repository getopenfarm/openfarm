namespace OpenFarm.Application.Auth;

/// <summary>How much a scope lets an app do. Printer control acts on a physical printer and is never pre-ticked.</summary>
public enum ScopeTier
{
    Read,
    Change,
    PrinterControl,
}

/// <summary>One OAuth scope: coarse on the wire, an exact permission list on the server.</summary>
/// <param name="Area">The access chip it groups under.</param>
/// <param name="Label">Consent-page text.</param>
/// <param name="Extra">The chip suffix for a printer-control scope (<c>Pause</c>, <c>Cancel</c>).</param>
public sealed record OAuthScope(string Scope, ScopeTier Tier, string Area, string Label, string? Extra, IReadOnlyList<string> Permissions);

/// <summary>An app's granted scopes for one area, as the Connected apps list shows them.</summary>
/// <param name="Level"><c>Read</c> or <c>Change</c>.</param>
/// <param name="Extras">Printer-control labels, for example <c>Pause</c>.</param>
/// <param name="Title">The consent labels of every granted scope in the area.</param>
/// <param name="Control">Whether any printer-control scope is granted.</param>
public sealed record AccessChip(string Area, string Level, string[] Extras, string Title, bool Control);

/// <summary>
/// The frozen OAuth scope catalog (dev-notes/MCP.md). Never widen a released scope: every app already granted it
/// would gain the new ability. Add a new scope instead; a snapshot test fails on any expansion change.
/// </summary>
public static class OAuthScopes
{
    public static readonly IReadOnlyList<OAuthScope> All =
    [
        new("printers.read", ScopeTier.Read, "Printers", "Read printers, their live status and feed units", null, ["printers:list", "printers:get-detail"]),
        new("print-jobs.read", ScopeTier.Read, "Queue", "Read the queue and print jobs", null, ["print-jobs:view"]),
        new("production-runs.read", ScopeTier.Read, "Runs", "Read production runs and their progress", null, ["production-runs:view"]),
        new("library.read", ScopeTier.Read, "Library", "Read parts, assemblies and library folders", null, ["parts:list", "parts:view", "parts:read", "assemblies:view", "library:view"]),
        new("filament.read", ScopeTier.Read, "Filament", "Read filament stock, forecasts, spools and farm stats", null, ["filaments:list", "spools:list"]),
        new("maintenance.read", ScopeTier.Read, "Maintenance", "Read maintenance tasks and what is due", null, ["maintenance:view"]),
        new("pricing.read", ScopeTier.Read, "Pricing", "Read fee profiles", null, ["settings:fees:read"]),
        new("print-jobs.write", ScopeTier.Change, "Queue", "Reorder, pin and requeue jobs", null,
            ["print-jobs:reorder", "print-jobs:pin", "print-jobs:requeue"]),
        new("production-runs.write", ScopeTier.Change, "Runs", "Plan and queue parts and assemblies, alone or as production runs", null, ["production-runs:create"]),
        new("library.write", ScopeTier.Change, "Library", "Organize folders, rename and tag parts, and add or change assemblies", null,
            ["library:manage", "parts:edit", "assemblies:create", "assemblies:edit"]),
        new("filament.write", ScopeTier.Change, "Filament", "Add new spools to stock", null, ["spools:register"]),
        new("maintenance.write", ScopeTier.Change, "Maintenance", "Add and assign maintenance tasks, and mark maintenance done", null, ["maintenance:complete", "maintenance:manage"]),
        new("pricing.write", ScopeTier.Change, "Pricing", "Add and change fee profiles", null, ["settings:fees:update"]),
        new("printers.ready", ScopeTier.PrinterControl, "Printers", "Mark printers ready after their plates are cleared", "Mark ready", ["printers:mark-ready"]),
        new("print-jobs.pause", ScopeTier.PrinterControl, "Queue", "Pause running prints", "Pause", ["print-jobs:remote-control"]),
        new("print-jobs.cancel", ScopeTier.PrinterControl, "Queue", "Cancel jobs and stop running prints", "Cancel", ["print-jobs:cancel"]),
    ];

    private static readonly Dictionary<string, OAuthScope> ByName = All.ToDictionary(s => s.Scope, StringComparer.Ordinal);

    public static OAuthScope? Find(string scope) => ByName.GetValueOrDefault(scope);

    /// <summary>Known scopes from a space-separated value, deduplicated, in catalog order. Unknown tokens are ignored.</summary>
    public static string[] Parse(string? scope)
    {
        var requested = (scope ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        return All.Where(s => requested.Contains(s.Scope)).Select(s => s.Scope).ToArray();
    }

    /// <summary>Every permission the given scopes allow. Unknown scopes allow nothing.</summary>
    public static IReadOnlySet<string> Expand(IEnumerable<string> scopes) =>
        scopes.Select(Find).OfType<OAuthScope>().SelectMany(s => s.Permissions).ToHashSet(StringComparer.Ordinal);

    /// <summary>A user may grant a scope when they hold any of its permissions; the app then gets only those they hold.</summary>
    public static bool Grantable(string scope, IReadOnlySet<string> userPermissions) =>
        Find(scope) is { } s && s.Permissions.Any(userPermissions.Contains);

    /// <summary><c>"{area label}: {permission label}"</c> for each permission a scope expands to.</summary>
    public static string[] Includes(OAuthScope scope) => scope.Permissions
        .Select(key => PermissionCatalog.All.Single(p => p.Key == key))
        .Select(p => $"{PermissionCatalog.Areas.Single(a => a.Key == p.Area).Label}: {p.Label}")
        .ToArray();

    /// <summary>Granted scopes grouped by area, in catalog order.</summary>
    public static AccessChip[] Chips(IEnumerable<string> scopes)
    {
        var granted = scopes.ToHashSet(StringComparer.Ordinal);
        return All.Where(s => granted.Contains(s.Scope)).GroupBy(s => s.Area)
            .Select(g => new AccessChip(
                g.Key,
                g.Any(s => s.Tier == ScopeTier.Change) ? "Change" : "Read",
                g.Select(s => s.Extra).OfType<string>().ToArray(),
                string.Join("; ", g.Select(s => s.Label)),
                g.Any(s => s.Tier == ScopeTier.PrinterControl)))
            .ToArray();
    }
}
