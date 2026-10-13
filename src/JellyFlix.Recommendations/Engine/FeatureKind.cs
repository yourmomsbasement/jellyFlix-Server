namespace JellyFlix.Recommendations.Engine;

/// <summary>
/// What kind of trait a feature describes. Each kind has its own weight in <see cref="FeatureWeights"/>.
/// </summary>
public enum FeatureKind
{
    /// <summary>A genre.</summary>
    Genre,

    /// <summary>A free-form tag.</summary>
    Tag,

    /// <summary>A studio or network.</summary>
    Studio,

    /// <summary>A director.</summary>
    Director,

    /// <summary>A writer.</summary>
    Writer,

    /// <summary>One of the top-billed actors.</summary>
    Actor,

    /// <summary>The decade the title came out in.</summary>
    Decade
}
