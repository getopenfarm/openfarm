using FluentAssertions;
using OpenFarm.Infrastructure.Profiles;

namespace OpenFarm.Infrastructure.Tests.Profiles;

public class ResourcesProfilesLocatorTests
{
    [Theory]
    [InlineData("blacklist.json")]
    [InlineData("Blacklist.json")]
    public void FindVendorIndexFiles_BlacklistWithVendorListKeys_IsExcluded(string blacklistFileName)
    {
        var bundle = Directory.CreateTempSubdirectory("profile-discovery-");
        try
        {
            var profiles = Directory.CreateDirectory(Path.Combine(bundle.FullName, "resources", "profiles"));
            File.WriteAllText(Path.Combine(profiles.FullName, blacklistFileName), """
                {
                  "filament": ["GFSA03"],
                  "process": ["GP008"],
                  "machine_model_list": [],
                  "process_list": [],
                  "filament_list": [],
                  "machine_list": []
                }
                """);
            var vendorPath = Path.Combine(profiles.FullName, "BBL.json");
            File.WriteAllText(vendorPath, """{"name":"Bambulab","machine_model_list":[]}""");
            // Leave unnamed vendor-shaped indexes for the ingestion service to diagnose.
            var malformedVendorPath = Path.Combine(profiles.FullName, "BrokenVendor.json");
            File.WriteAllText(malformedVendorPath, """{"machine_list":[]}""");

            var result = ResourcesProfilesLocator.FindVendorIndexFiles(bundle.FullName);

            result.Should().Equal(vendorPath, malformedVendorPath);
        }
        finally
        {
            bundle.Delete(recursive: true);
        }
    }
}
