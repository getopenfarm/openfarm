using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts;

/// <summary>Bounded, process-local evidence. A restart or changed settings requires another validation.</summary>
public sealed class ProfileEngineValidationCache : IDisposable
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    private readonly Dictionary<string, (DateTimeOffset Expires, string? Issue)> _results = [];

    internal bool TryGet(string key, out string? issue)
    {
        issue = null;
        if (!_results.TryGetValue(key, out var entry) || entry.Expires <= DateTimeOffset.UtcNow) return false;
        issue = entry.Issue;
        return true;
    }

    internal void Record(string key, string? issue)
    {
        if (_results.Count >= 128) _results.Remove(_results.MinBy(row => row.Value.Expires).Key);
        _results[key] = (DateTimeOffset.UtcNow.Add(issue is null ? TimeSpan.FromHours(24) : TimeSpan.FromMinutes(1)), issue);
    }

    public void Dispose() => Gate.Dispose();
}

public sealed class ProfileEngineValidator(ISlicerEngine engine, ProfileEngineValidationCache cache) : IProfileEngineValidator
{
    // A tiny closed tetrahedron exercises preset loading, custom G-code and actual toolpath generation.
    private static readonly byte[] ValidationModel = Encoding.UTF8.GetBytes("""
        solid profile_validation
        facet normal 0 0 -1
          outer loop
            vertex 0 0 0
            vertex 0 5 0
            vertex 5 0 0
          endloop
        endfacet
        facet normal 0 -1 0
          outer loop
            vertex 0 0 0
            vertex 5 0 0
            vertex 0 0 5
          endloop
        endfacet
        facet normal -1 0 0
          outer loop
            vertex 0 0 0
            vertex 0 0 5
            vertex 0 5 0
          endloop
        endfacet
        facet normal 1 1 1
          outer loop
            vertex 5 0 0
            vertex 0 5 0
            vertex 0 0 5
          endloop
        endfacet
        endsolid profile_validation
        """);

    public async Task ValidateAsync(SlicerRef slicer, ResolvedSlicingProfiles profiles, string materialType,
        decimal nozzleDiameterMm, CancellationToken cancellationToken)
    {
        ProfileEngineCompatibility.EnsureSupported(slicer.Engine, profiles.MachineSourceFormat,
            profiles.ProcessSourceFormat, profiles.FilamentSourceFormat);
        if (new[] { profiles.MachineSourceFormat, profiles.ProcessSourceFormat, profiles.FilamentSourceFormat }
            .All(source => ProfileEngineCompatibility.NativeEngine(source) == slicer.Engine)) return;

        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { slicer, profiles, materialType, nozzleDiameterMm })));
        await cache.Gate.WaitAsync(cancellationToken);
        try
        {
            if (!cache.TryGet(fingerprint, out var issue))
            {
                try
                {
                    using var model = new MemoryStream(ValidationModel, writable: false);
                    var result = await engine.SliceConfiguredAsync(slicer, model, PartFileFormat.Stl, materialType,
                        nozzleDiameterMm, Guid.Empty, profiles, cancellationToken);
                    if (result.OutputFileBytes.Length == 0) throw new SlicingFailedException("The validation slice produced no output.");
                    cache.Record(fingerprint, null);
                }
                catch (SlicingFailedException ex)
                {
                    issue = $"Profile compatibility validation failed with {slicer}: {ex.Message} Select a native preset or update the imported profile.";
                    cache.Record(fingerprint, issue);
                }
            }
            if (issue is not null) throw new SlicingProfileUnconfiguredException(issue);
        }
        finally { cache.Gate.Release(); }
    }
}
