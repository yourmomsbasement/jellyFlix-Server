using System.Collections.Generic;

namespace JellyFlix.Recommendations.Engine;

/// <summary>
/// A "Because you watched {Seed}" row.
/// </summary>
/// <param name="Seed">The title the row is based on.</param>
/// <param name="Titles">Titles similar to the seed, best first.</param>
public record SeedRow(Title Seed, IReadOnlyList<ScoredTitle> Titles);
