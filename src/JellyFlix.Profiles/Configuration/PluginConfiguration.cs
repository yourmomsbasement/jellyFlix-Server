using MediaBrowser.Model.Plugins;

namespace JellyFlix.Profiles.Configuration;

/// <summary>
/// Server-wide settings for household profiles.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the maximum number of profiles in a household, including the owner's own profile.
    /// </summary>
    public int MaxProfilesPerHousehold { get; set; } = 5;

    /// <summary>
    /// Gets or sets a value indicating whether non-administrator accounts may create profiles.
    /// </summary>
    public bool AllowNonAdminOwners { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum parental rating score applied to kids profiles (10 = PG / TV-PG in the US rating system).
    /// </summary>
    public int KidsMaxParentalRating { get; set; } = 10;
}
