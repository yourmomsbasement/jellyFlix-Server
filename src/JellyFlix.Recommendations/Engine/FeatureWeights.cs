namespace JellyFlix.Recommendations.Engine;

/// <summary>
/// How much each kind of shared trait says about two titles being alike.
/// A shared director is a much stronger hint than a shared decade.
/// </summary>
public static class FeatureWeights
{
    /// <summary>
    /// Gets the weight of a feature kind.
    /// </summary>
    /// <param name="kind">The feature kind.</param>
    /// <returns>The weight.</returns>
    public static double Of(FeatureKind kind) => kind switch
    {
        FeatureKind.Genre => 1.0,
        FeatureKind.Director => 1.2,
        FeatureKind.Writer => 0.6,
        FeatureKind.Actor => 0.6,
        FeatureKind.Tag => 0.5,
        FeatureKind.Studio => 0.4,
        FeatureKind.Decade => 0.3,
        _ => 0.5
    };
}
