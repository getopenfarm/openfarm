using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

// Shared parsing for the three US2/US4 picker queries' raw `origin` string (contracts/profiles-api.md).
public static class ProfileOriginQueryParser
{
    // Only meaningful once the caller already knows it's on the picker path (some query param was
    // present) — an absent `origin` still defaults to "User" there, per the contract's stated
    // default. "All" means no origin filter at all, which is why it can't collapse to the same
    // `null` as "absent" the way a plain `ProfileOrigin?` would.
    public static ProfileOrigin? ParseEffectiveFilter(string? origin) => origin switch
    {
        null => ProfileOrigin.User,
        "All" => null,
        "System" => ProfileOrigin.System,
        "User" => ProfileOrigin.User,
        _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, "origin must be one of \"System\", \"User\", or \"All\"."),
    };
}
