namespace OpenFarm.Domain.Maintenance;

/// <summary>
/// Names of the maintenance tasks the MaintenanceTracker migration seeds. They are ordinary tasks once seeded: editable,
/// deletable, never re-created.
/// </summary>
public static class DefaultMaintenance
{
    public const string XyAxisHighUse = "XY-Axis Cleaning & Lubrication (High Use)";
    public const string AirFilter = "Air Filter Replacement";
    public const string CarbonRods = "Carbon Rod Cleaning";
    public const string ZLeadScrew = "Z-Axis Lead Screw Lubrication";
    public const string CameraLens = "Camera Lens Cleaning";
    public const string XyAxis = "XY-Axis Cleaning & Lubrication";
    public const string Calibration = "Full Calibration Run";
    public const string RailLubrication = "Rail / Axis Lubrication";
    public const string ExtruderGearStandard = "Extruder Gear Cleaning — Standard";
    public const string ExtruderGearCf = "Extruder Gear Cleaning — CF Filament";

    public static readonly IReadOnlyList<string> Names =
    [
        XyAxisHighUse, AirFilter, CarbonRods, ZLeadScrew, CameraLens, XyAxis, Calibration, RailLubrication, ExtruderGearStandard, ExtruderGearCf,
    ];
}
