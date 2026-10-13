namespace JellyFlix.Profiles.Api.Models;

/// <summary>
/// A request to switch to another profile.
/// </summary>
public class SwitchProfileDto
{
    /// <summary>
    /// Gets or sets the target profile's PIN, when it has one.
    /// </summary>
    public string? Pin { get; set; }
}
