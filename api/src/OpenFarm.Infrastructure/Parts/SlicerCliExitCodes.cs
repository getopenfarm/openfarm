namespace OpenFarm.Infrastructure.Parts;

/// <summary>
/// Plain-language meaning of the exit codes OrcaSlicer's and Bambu Studio's CLIs return (the <c>CLI_*</c>
/// defines in <c>src/libslic3r/Utils.hpp</c>, identical in both). .NET reports a Unix exit status as
/// 0-255, so the CLI's negative codes arrive as 256 + code (-24 → 232); Windows keeps them negative.
/// </summary>
internal static class SlicerCliExitCodes
{
    public static string? Describe(int exitCode)
    {
        // Signal deaths are reported as 128 + signal and carry no CLI message (and the empty stderr that goes with it).
        switch (exitCode)
        {
            case 134: return "the slicer aborted (SIGABRT)";
            case 137: return "the slicer was killed (SIGKILL, often out of memory)";
            case 139: return "the slicer crashed (SIGSEGV)";
        }

        return (exitCode is > 127 and <= 255 ? exitCode - 256 : exitCode) switch
        {
            -2 => "invalid command-line parameters",
            -3 => "the input file was not found",
            -5 => "a preset file is invalid or could not be parsed",
            -6 => "the model file could not be parsed",
            -14 => "out of memory",
            -16 => "the 3MF's printer does not support the selected machine",
            -17 => "the process preset is not compatible with the machine preset",
            -23 => "the 3MF has modified printer parameters",
            -24 => "the 3MF was saved by a newer or incompatible slicer version",
            -50 => "a plate is empty or has no object fully inside it",
            -51 => "the slicing parameters failed validation",
            -52 => "an object lies over the bed boundary",
            -58 => "slicing time per plate exceeded the limit",
            -59 => "triangle count per plate exceeded the limit",
            -61 => "the filament is not compatible with the plate type",
            -62 => "the filaments' temperatures differ too much",
            -63 or -64 => "objects collide",
            -65 => "settings are invalid for spiral vase mode",
            -100 => "slicing failed",
            -101 => "G-code path conflicts were detected",
            _ => null,
        };
    }
}
