using System.Collections.Generic;

namespace JellyFlix.Recommendations.Api.Models;

/// <summary>
/// Personalized rows for Home.
/// </summary>
public class RecommendationsDto
{
    /// <summary>
    /// Gets or sets a value indicating whether the picks are based on the profile's history. When false they are
    /// simply well-rated and recently added titles, because the profile hasn't watched anything yet.
    /// </summary>
    public bool IsPersonalized { get; set; }

    /// <summary>
    /// Gets or sets the "Top Picks" titles, best first.
    /// </summary>
    public IReadOnlyList<RecommendedItemDto> Recommended { get; set; } = [];

    /// <summary>
    /// Gets or sets the "Because you watched" rows, most recent first.
    /// </summary>
    public IReadOnlyList<SeedRowDto> BecauseYouWatched { get; set; } = [];
}
