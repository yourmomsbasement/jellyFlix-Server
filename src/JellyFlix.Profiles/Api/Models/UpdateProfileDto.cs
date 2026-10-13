namespace JellyFlix.Profiles.Api.Models;

/// <summary>
/// A request to change a profile. Omitted fields are left unchanged.
/// </summary>
public class UpdateProfileDto
{
    /// <summary>
    /// Gets or sets the new display name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the new avatar color as #RRGGBB.
    /// </summary>
    public string? AvatarColor { get; set; }

    /// <summary>
    /// Gets or sets whether this is a kids profile. Only the household owner may change it.
    /// </summary>
    public bool? IsKids { get; set; }

    /// <summary>
    /// Gets or sets a new four-digit PIN.
    /// </summary>
    public string? Pin { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to remove the PIN.
    /// </summary>
    public bool RemovePin { get; set; }
}
