namespace JellyFlix.Profiles.Api.Models;

/// <summary>
/// A request to add a profile.
/// </summary>
public class CreateProfileDto
{
    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the avatar color as #RRGGBB. A color is picked when omitted.
    /// </summary>
    public string? AvatarColor { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is a kids profile.
    /// </summary>
    public bool IsKids { get; set; }

    /// <summary>
    /// Gets or sets an optional four-digit PIN.
    /// </summary>
    public string? Pin { get; set; }
}
