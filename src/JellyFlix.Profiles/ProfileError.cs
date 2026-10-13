namespace JellyFlix.Profiles;

/// <summary>
/// Why a profile operation was refused.
/// </summary>
public enum ProfileError
{
    /// <summary>
    /// The request was malformed.
    /// </summary>
    BadRequest,

    /// <summary>
    /// The caller may not perform the operation.
    /// </summary>
    Forbidden,

    /// <summary>
    /// The profile does not exist in the caller's household.
    /// </summary>
    NotFound,

    /// <summary>
    /// The household already has the maximum number of profiles.
    /// </summary>
    ProfileLimitReached,

    /// <summary>
    /// The profile needs a PIN and none or a wrong one was given.
    /// </summary>
    InvalidPin,

    /// <summary>
    /// Too many wrong PINs; the profile is locked for a while.
    /// </summary>
    PinLocked
}
