using FluentAssertions;
using OpenFarm.Infrastructure.Parts;

namespace OpenFarm.Infrastructure.Tests.Parts;

// The CLI's negative CLI_* codes reach .NET as 256 + code on Unix (232 = -24) and stay negative on Windows.
public class SlicerCliExitCodesTests
{
    [Theory]
    [InlineData(232, "newer or incompatible slicer version")] // CLI_FILE_VERSION_NOT_SUPPORTED (-24)
    [InlineData(-24, "newer or incompatible slicer version")] // same code on Windows
    [InlineData(239, "not compatible with the machine preset")] // CLI_PROCESS_NOT_COMPATIBLE (-17)
    [InlineData(251, "preset file is invalid")] // CLI_CONFIG_FILE_ERROR (-5)
    [InlineData(195, "not compatible with the plate type")] // CLI_FILAMENT_NOT_MATCH_BED_TYPE (-61)
    [InlineData(156, "slicing failed")] // CLI_SLICING_ERROR (-100)
    [InlineData(139, "SIGSEGV")]
    public void Describe_KnownCodes_ExplainThem(int exitCode, string expectedFragment) =>
        SlicerCliExitCodes.Describe(exitCode).Should().Contain(expectedFragment);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(0)]
    public void Describe_UnknownCodes_AreLeftAlone(int exitCode) =>
        SlicerCliExitCodes.Describe(exitCode).Should().BeNull();
}
