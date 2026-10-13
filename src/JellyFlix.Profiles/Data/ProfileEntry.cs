using System;

namespace JellyFlix.Profiles.Data;

/// <summary>
/// A profile in a household. Each profile is backed by its own Jellyfin user, so playback state is tracked per profile.
/// </summary>
public class ProfileEntry
{
    /// <summary>
    /// Gets or sets the id of the Jellyfin user backing this profile.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the name shown on the profile picker.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the avatar color as #RRGGBB.
    /// </summary>
    public string AvatarColor { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the salted PIN hash, or null when the profile has no PIN.
    /// </summary>
    public string? PinHash { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is a kids profile.
    /// </summary>
    public bool IsKids { get; set; }
}
