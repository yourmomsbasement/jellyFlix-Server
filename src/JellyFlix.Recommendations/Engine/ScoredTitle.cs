using System.Collections.Generic;

namespace JellyFlix.Recommendations.Engine;

/// <summary>
/// A recommended title with its score and why it was picked.
/// </summary>
/// <param name="Title">The title.</param>
/// <param name="Score">The ranking score (higher is better).</param>
/// <param name="Reasons">The shared traits that drove the pick, strongest first, e.g. "Thriller".</param>
public record ScoredTitle(Title Title, double Score, IReadOnlyList<string> Reasons);
