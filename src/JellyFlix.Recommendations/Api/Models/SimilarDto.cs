using System.Collections.Generic;
using MediaBrowser.Model.Dto;

namespace JellyFlix.Recommendations.Api.Models;

/// <summary>
/// "More Like This" for a title, plus how well the title matches the profile.
/// </summary>
public class SimilarDto
{
    /// <summary>
    /// Gets or sets the Netflix-style match percentage (60-98), or null when the profile has no history yet.
    /// </summary>
    public int? Match { get; set; }

    /// <summary>
    /// Gets or sets the similar titles, best first.
    /// </summary>
    public IReadOnlyList<BaseItemDto> Items { get; set; } = [];
}
