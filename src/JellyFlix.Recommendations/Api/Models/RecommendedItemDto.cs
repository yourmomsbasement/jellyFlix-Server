using System.Collections.Generic;
using MediaBrowser.Model.Dto;

namespace JellyFlix.Recommendations.Api.Models;

/// <summary>
/// A recommended title and why it was picked.
/// </summary>
public class RecommendedItemDto
{
    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public BaseItemDto Item { get; set; } = null!;

    /// <summary>
    /// Gets or sets the traits it shares with the profile's taste, strongest first, e.g. "Thriller".
    /// </summary>
    public IReadOnlyList<string> Reasons { get; set; } = [];
}
