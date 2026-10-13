using System;

namespace JellyFlix.Profiles.Api.Models;

/// <summary>
/// A profile as shown on the profile picker.
/// </summary>
public class ProfileDto
{
    /// <summary>
    /// Gets or sets the profile id. This is also the id of the Jellyfin user backing the profile.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the avatar color as #RRGGBB.
    /// </summary>
    public string AvatarColor { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the primary image tag of the backing user, if it has an avatar image.
    /// </summary>
    public string? PrimaryImageTag { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether switching to this profile requires a PIN.
    /// </summary>
    public bool HasPin { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is a kids profile.
    /// </summary>
    public bool IsKids { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is the household owner's profile.
    /// </summary>
    public bool IsOwner { get; set; }
}
