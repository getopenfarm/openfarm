using System.Security.Cryptography;

namespace OpenFarm.Application.Parts;

/// <summary>
/// Computes the SHA-256 hex hash <see cref="IPartFileStorage"/>'s content-addressed path convention
/// keys on (research.md R6) — hashing happens here, in the Application-layer caller, the same place
/// <c>StoragePath</c> is already resolved, so <c>Part.Create</c>/<c>ReplaceFile</c> receive an
/// already-computed hash rather than hashing themselves.
/// </summary>
public static class PartFileHasher
{
    public static async Task<string> ComputeHashAsync(Stream content, CancellationToken cancellationToken)
    {
        var hash = await SHA256.HashDataAsync(content, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }
}
