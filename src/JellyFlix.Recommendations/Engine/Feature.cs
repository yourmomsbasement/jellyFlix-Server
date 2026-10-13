namespace JellyFlix.Recommendations.Engine;

/// <summary>
/// One trait of a title, such as a genre or a director.
/// </summary>
/// <param name="Kind">The kind of trait.</param>
/// <param name="Name">The display name, e.g. "Thriller".</param>
public record Feature(FeatureKind Kind, string Name)
{
    /// <summary>
    /// Gets the key used to compare features across titles (case-insensitive name within a kind).
    /// </summary>
    public string Key { get; } = Kind + ":" + Name.ToUpperInvariant();
}
