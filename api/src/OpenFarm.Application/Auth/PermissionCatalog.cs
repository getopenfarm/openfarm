namespace OpenFarm.Application.Auth;

/// <summary>One area of the app, in sidebar order. <paramref name="Noun"/> ends a summary sentence ("Can see and add printers").</summary>
public sealed record PermissionArea(string Key, string Label, string Noun, bool AdministratorOnly);

/// <summary>
/// One grantable ability. Its level tier is derived: <see cref="IsRead"/> is View, <see cref="IsDestructive"/> is Full,
/// anything else is Edit. <see cref="Verb"/> goes before the area's noun in a summary sentence; <see cref="Phrase"/> stands
/// on its own. An explicit ability belongs to no level and is only ever granted by name.
/// </summary>
public sealed record PermissionDefinition(string Key, string Area, string Label, string Description, bool IsRead, bool IsSensitive,
    bool IsDestructive, bool IsExplicit = false, string? Verb = null, string? Phrase = null);

public static class PermissionCatalog
{
    public const string Administrators = "Administrators";
    public const string GrantClaim = "openfarm.permission";

    public static readonly IReadOnlyList<PermissionArea> Areas =
    [
        new("printers", "Printers", "printers", false),
        new("print-jobs", "Print jobs", "print jobs", false),
        new("production-runs", "Production runs", "production runs", false),
        new("parts", "Parts", "parts", false),
        new("assemblies", "Assemblies", "assemblies", false),
        new("non-printed-components", "Non-printed components", "non-printed components", false),
        new("library", "Library", "library folders", false),
        new("spools", "Spools", "spools", false),
        new("filaments", "Filaments", "filaments", false),
        new("vendors", "Vendors", "vendors", false),
        new("feed-units", "Feed units", "feed units", false),
        new("profiles", "Slicer profiles", "slicer profiles", false),
        new("tags", "Tags", "tags", false),
        new("settings", "Settings", "settings", false),
        new("activity-log", "Activity log", "the activity log", false),
        new("users", "Users & Groups", "users and groups", true),
    ];

    // Per area: reads, add, change, special abilities, destructive.
    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new("printers:list", "printers", "List", "See the fleet with live status and camera snapshots.", true, false, false, Verb: "list"),
        new("printers:get-detail", "printers", "View", "Open a printer to see its full status, feed units and settings.", true, false, false, Verb: "see"),
        new("printers:register", "printers", "Add", "Discover printers on the network and add them to the fleet.", false, false, false, Verb: "add"),
        new("printers:mark-ready", "printers", "Mark ready", "Confirm a build plate is clear so the printer can take its next job.", false, false, false, Phrase: "mark printers ready"),
        new("printers:verify-connection", "printers", "Verify connection", "Test a printer address and access code from the server before saving them.", false, true, false, Phrase: "verify printer connections"),
        new("printers:edit-connection", "printers", "Change connection", "Rename a printer and change its address, access code and profiles. A wrong value takes it offline.", false, true, false, IsExplicit: true, Phrase: "change printer connections"),
        new("printers:remove", "printers", "Remove", "Remove a printer from the fleet.", false, false, true, Verb: "remove"),

        new("print-jobs:view", "print-jobs", "View", "See the queue, job details, objects and print files.", true, false, false, Verb: "see"),
        new("print-jobs:pin", "print-jobs", "Pin", "Pin a job to a printer, unpin it, and set its filament requirements.", false, false, false, Verb: "pin"),
        new("print-jobs:reorder", "print-jobs", "Reorder", "Move jobs up or down the queue, or move a whole run to the top.", false, false, false, Verb: "reorder"),
        new("print-jobs:requeue", "print-jobs", "Requeue", "Put failed or cancelled jobs back in the queue.", false, false, false, Verb: "requeue"),
        new("print-jobs:update-status", "print-jobs", "Update status", "Mark a job started, completed or failed by hand, and dismiss its notifications.", false, false, false, Phrase: "update job status"),
        new("print-jobs:remote-control", "print-jobs", "Remote control", "Pause, resume and skip objects on a running print. Each command acts on the printer at once.", false, true, false, IsExplicit: true, Phrase: "control prints remotely"),
        new("print-jobs:cancel", "print-jobs", "Cancel", "Cancel a queued job, or stop one that is printing.", false, false, true, Verb: "cancel"),
        new("print-jobs:write-off", "print-jobs", "Write off", "Accept a failed or cancelled plate as lost instead of printing it again.", false, false, true, Verb: "write off"),

        new("production-runs:view", "production-runs", "View", "See production runs, their progress and the backfill list.", true, false, false, Verb: "see"),
        new("production-runs:create", "production-runs", "Add", "Plan and submit new production runs.", false, false, false, Verb: "add"),
        new("production-runs:edit", "production-runs", "Change", "Change a run's details and lines, and change backfill.", false, false, false, Verb: "change"),
        new("production-runs:close", "production-runs", "Close", "Sign off a run that has no work left.", false, false, true, Verb: "close"),
        new("production-runs:cancel", "production-runs", "Cancel", "Cancel every queued job of a run. Jobs already printing continue.", false, false, true, Verb: "cancel"),

        new("parts:list", "parts", "List", "See the parts list.", true, false, false, Verb: "list"),
        new("parts:view", "parts", "View", "See the objects and object groups of a part.", true, false, false, Verb: "see"),
        new("parts:read", "parts", "Download", "Open a part, see its processing, and download its file.", true, false, false, Phrase: "download part files"),
        new("parts:create", "parts", "Add", "Upload new parts and confirm or discard their processing.", false, false, false, Verb: "add"),
        new("parts:edit", "parts", "Change", "Change a part's details and object groups.", false, false, false, Verb: "change"),
        new("parts:replace-file", "parts", "Replace file", "Upload a new file for a part, or process its file again.", false, false, false, Phrase: "replace part files"),
        new("parts:delete", "parts", "Delete", "Delete parts.", false, false, true, Verb: "delete"),

        new("assemblies:view", "assemblies", "View", "See assemblies and their bills of materials.", true, false, false, Verb: "see"),
        new("assemblies:create", "assemblies", "Add", "Add assemblies.", false, false, false, Verb: "add"),
        new("assemblies:edit", "assemblies", "Change", "Change an assembly's details and bill of materials.", false, false, false, Verb: "change"),
        new("assemblies:delete", "assemblies", "Delete", "Delete assemblies.", false, false, true, Verb: "delete"),

        new("non-printed-components:view", "non-printed-components", "View", "See non-printed components and cost items.", true, false, false, Verb: "see"),
        new("non-printed-components:create", "non-printed-components", "Add", "Add non-printed components and cost items.", false, false, false, Verb: "add"),
        new("non-printed-components:edit", "non-printed-components", "Change", "Change non-printed components and cost items.", false, false, false, Verb: "change"),
        new("non-printed-components:delete", "non-printed-components", "Delete", "Delete non-printed components and cost items.", false, false, true, Verb: "delete"),

        new("library:view", "library", "View", "Browse the folders of the parts and assemblies libraries.", true, false, false, Verb: "see"),
        new("library:manage", "library", "Organize", "Add, rename and move folders, and move items between them.", false, false, false, Verb: "organize"),
        new("library:delete", "library", "Delete", "Delete library folders.", false, false, true, Verb: "delete"),

        new("spools:list", "spools", "View", "See spools, their history and the material reports.", true, false, false, Verb: "see"),
        new("spools:register", "spools", "Add", "Add new spools to stock.", false, false, false, Verb: "add"),
        new("spools:load", "spools", "Load", "Load spools into printers or feed units, and attach RFID tags.", false, false, false, Verb: "load"),
        new("spools:unload", "spools", "Unload", "Unload spools, and put finished spools back in storage.", false, false, false, Verb: "unload"),
        new("spools:record-usage", "spools", "Record usage", "Record filament used from a spool, and assign spools to printed lines.", false, false, false, Phrase: "record filament usage"),
        new("spools:override-weight", "spools", "Override weight", "Set a spool's remaining weight by hand, replacing the tracked value.", false, false, true, Phrase: "override spool weights"),

        new("filaments:list", "filaments", "View", "See filaments, shortages and their profile assignments.", true, false, false, Verb: "see"),
        new("filaments:create", "filaments", "Add", "Add filaments.", false, false, false, Verb: "add"),
        new("filaments:assign-profiles", "filaments", "Change", "Change filaments and the slicer profiles assigned to them.", false, false, false, Verb: "change"),

        new("vendors:list", "vendors", "View", "See vendors.", true, false, false, Verb: "see"),
        new("vendors:create", "vendors", "Add", "Add vendors.", false, false, false, Verb: "add"),

        new("feed-units:list", "feed-units", "View", "See the feed units of a printer.", true, false, false, Verb: "see"),
        new("feed-units:add", "feed-units", "Add", "Add feed units to a printer.", false, false, false, Verb: "add"),
        new("feed-units:configure", "feed-units", "Configure", "Configure a feed unit and select its feeding slot.", false, false, false, Verb: "configure"),
        new("feed-units:remove", "feed-units", "Remove", "Remove feed units from a printer.", false, false, true, Verb: "remove"),

        new("profiles:list-machine", "profiles", "List machine", "See the list of machine profiles.", true, false, false, Phrase: "list machine profiles"),
        new("profiles:get-machine", "profiles", "View machine", "Open a machine profile and its settings.", true, false, false, Phrase: "open machine profiles"),
        new("profiles:list-process", "profiles", "List process", "See the list of process profiles.", true, false, false, Phrase: "list process profiles"),
        new("profiles:get-process", "profiles", "View process", "Open a process profile and its settings.", true, false, false, Phrase: "open process profiles"),
        new("profiles:list-filament", "profiles", "List filament", "See the list of filament profiles.", true, false, false, Phrase: "list filament profiles"),
        new("profiles:get-filament", "profiles", "View filament", "Open a filament profile and its settings.", true, false, false, Phrase: "open filament profiles"),
        new("profiles:preview-import", "profiles", "Preview import", "Upload profile files and see what an import would add.", false, false, false, Phrase: "preview imports"),
        new("profiles:confirm-import", "profiles", "Import", "Add previewed profiles to the farm.", false, false, false, Phrase: "import profiles"),
        new("profiles:edit-machine-raw-data", "profiles", "Change machine data", "Change the raw settings of a user machine profile.", false, false, false, Phrase: "change machine profile data"),
        new("profiles:edit-filament-raw-data", "profiles", "Change filament data", "Change the raw settings of a user filament profile.", false, false, false, Phrase: "change filament profile data"),

        new("tags:list", "tags", "View", "See tags.", true, false, false, Verb: "see"),

        new("settings:general:read", "settings", "View general", "See the farm's general settings.", true, false, false, Phrase: "see general settings"),
        new("settings:fees:read", "settings", "View fees", "See fees and fee profiles.", true, false, false, Phrase: "see fees"),
        new("settings:printer-costs:read", "settings", "View printer costs", "See the electricity price and the cost of each printer model.", true, false, false, Phrase: "see printer costs"),
        new("settings:slicing-defaults:read", "settings", "View slicing defaults", "See the slicing defaults and slicing readiness.", true, false, false, Phrase: "see slicing defaults"),
        new("settings:general:update", "settings", "Change general", "Change the farm's general settings.", false, true, false, Phrase: "change general settings"),
        new("settings:fees:update", "settings", "Change fees", "Change fees and add, change or remove fee profiles. This changes every quoted cost.", false, true, false, Phrase: "change fees"),
        new("settings:printer-costs:update", "settings", "Change printer costs", "Change the electricity price and printer model costs. This changes every quoted cost.", false, true, false, Phrase: "change printer costs"),
        new("settings:slicing-defaults:update", "settings", "Change slicing defaults", "Change the slicer and profiles every part is sliced with.", false, true, false, Phrase: "change slicing defaults"),

        new("activity-log:list", "activity-log", "View", "See who changed what across the farm, including other users' actions.", true, true, false, IsExplicit: true, Verb: "see"),

        new("users:manage", "users", "Manage", "Add, change and remove users and groups, and sign users out.", false, true, false, Phrase: "manage users and groups"),
    ];

    private static readonly Dictionary<string, int> Index = All.Select((p, i) => (p.Key, i)).ToDictionary(x => x.Key, x => x.i, StringComparer.Ordinal);
    private static readonly HashSet<string> AdministratorOnlyAreas = Areas.Where(a => a.AdministratorOnly).Select(a => a.Key).ToHashSet(StringComparer.Ordinal);

    public static bool IsKnown(string key) => Index.ContainsKey(key);
    public static bool IsGrantable(string key) => Index.TryGetValue(key, out var i) && !AdministratorOnlyAreas.Contains(All[i].Area);

    /// <summary>Keys in catalog order; unknown keys last, in ordinal order.</summary>
    public static string[] Sort(IEnumerable<string> keys) =>
        keys.OrderBy(k => Index.TryGetValue(k, out var i) ? i : int.MaxValue).ThenBy(k => k, StringComparer.Ordinal).ToArray();
}
