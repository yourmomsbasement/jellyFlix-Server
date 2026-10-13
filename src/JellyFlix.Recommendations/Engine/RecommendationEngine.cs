using System;
using System.Collections.Generic;
using System.Linq;

namespace JellyFlix.Recommendations.Engine;

/// <summary>
/// Content-based recommendations for one profile over one catalog.
/// </summary>
/// <remarks>
/// <para>
/// Every title becomes a vector of its traits (genres, tags, studios, directors, writers, top cast, decade). Each trait
/// is weighted by its kind (<see cref="FeatureWeights"/>) and by how rare it is in the library (inverse document
/// frequency), so sharing an unusual director counts for more than sharing "Drama".
/// </para>
/// <para>
/// The profile's taste is the weighted sum of the vectors of titles it watched, favorited or liked, with recent
/// activity counting more and dislikes pulling away. Unwatched titles are ranked by cosine similarity to that taste,
/// nudged by community rating and how recently they were added, then re-ranked with maximal marginal relevance so a
/// row isn't ten titles from the same franchise.
/// </para>
/// <para>
/// With no taste yet (a new profile), titles are ranked by rating and freshness instead.
/// </para>
/// </remarks>
public sealed class RecommendationEngine
{
    /// <summary>Taste half-life: activity this many days old counts half as much.</summary>
    private const double TasteHalfLifeDays = 120;

    /// <summary>Old activity never drops below this share, so long-time favorites keep counting.</summary>
    private const double MinRecencyFactor = 0.25;

    /// <summary>Weight for signals with no activity date (e.g. a favorite that was never played).</summary>
    private const double UndatedRecencyFactor = 0.6;

    /// <summary>How quickly "recently added" stops mattering, in days.</summary>
    private const double FreshnessDecayDays = 45;

    private const double TasteWeight = 0.80;
    private const double QualityWeight = 0.12;
    private const double FreshnessWeight = 0.08;

    /// <summary>Relevance vs. variety for the main list (1 = relevance only).</summary>
    private const double RecommendDiversity = 0.7;

    /// <summary>Seed rows and "More Like This" stay closer to the seed.</summary>
    private const double SimilarDiversity = 0.85;

    /// <summary>Titles less similar than this to a seed are not "like" it.</summary>
    private const double MinSeedSimilarity = 0.12;

    private readonly Dictionary<Guid, Title> _titles;
    private readonly Dictionary<Guid, SparseVector> _vectors;
    private readonly Dictionary<string, string> _featureNames;
    private readonly double _meanQuality;
    private readonly DateTime _now;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecommendationEngine"/> class.
    /// </summary>
    /// <param name="catalog">Every title the profile may see.</param>
    /// <param name="now">The current time.</param>
    public RecommendationEngine(IReadOnlyCollection<Title> catalog, DateTime now)
    {
        _now = now;
        _titles = catalog.ToDictionary(t => t.Id);
        _featureNames = new Dictionary<string, string>(StringComparer.Ordinal);

        // Document frequency: how many titles have each trait
        var documentFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var title in catalog)
        {
            foreach (var key in title.Features.Select(f => f.Key).Distinct())
            {
                documentFrequency[key] = documentFrequency.GetValueOrDefault(key) + 1;
            }

            foreach (var feature in title.Features)
            {
                _featureNames.TryAdd(feature.Key, feature.Name);
            }
        }

        var count = Math.Max(1, catalog.Count);
        _vectors = catalog.ToDictionary(
            t => t.Id,
            t => BuildVector(t, key => Math.Log(1 + ((double)count / documentFrequency[key]))));

        var ratings = catalog.Where(t => t.CommunityRating.HasValue).Select(t => (double)t.CommunityRating!.Value / 10).ToList();
        _meanQuality = ratings.Count > 0 ? ratings.Average() : 0.6;
    }

    /// <summary>
    /// Builds a profile's taste vector from its signals.
    /// </summary>
    /// <param name="signals">The profile's signals.</param>
    /// <returns>The unit-length taste vector; empty when there is no positive evidence.</returns>
    public SparseVector BuildTaste(IEnumerable<TasteSignal> signals)
    {
        var taste = new SparseVector();
        var hasPositive = false;
        foreach (var signal in signals)
        {
            if (!_vectors.TryGetValue(signal.TitleId, out var vector))
            {
                continue;
            }

            hasPositive |= signal.Weight > 0;
            taste.Add(vector, signal.Weight * RecencyFactor(signal.LastActivity));
        }

        if (!hasPositive)
        {
            return new SparseVector();
        }

        // Drop traits the profile is net-negative on; they only push the taste away from everything.
        var positive = new SparseVector();
        foreach (var (key, value) in taste.Values.Where(kv => kv.Value > 0))
        {
            positive.Set(key, value);
        }

        return positive.Normalized();
    }

    /// <summary>
    /// Picks titles for the profile, best first.
    /// </summary>
    /// <param name="taste">The profile's taste from <see cref="BuildTaste"/>.</param>
    /// <param name="exclude">Titles not to recommend (already watched, in progress, disliked).</param>
    /// <param name="limit">How many to return.</param>
    /// <returns>The recommendations.</returns>
    public IReadOnlyList<ScoredTitle> Recommend(SparseVector taste, IReadOnlySet<Guid> exclude, int limit)
    {
        var hasTaste = !taste.IsEmpty;
        var candidates = _titles.Values
            .Where(t => !exclude.Contains(t.Id))
            .Select(t =>
            {
                var score = hasTaste
                    ? (TasteWeight * taste.Dot(_vectors[t.Id])) + (QualityWeight * Quality(t)) + (FreshnessWeight * Freshness(t))
                    : (0.6 * Quality(t)) + (0.4 * Freshness(t)) + (0.05 * DailyJitter(t.Id));
                return new ScoredTitle(t, score, hasTaste ? Reasons(taste, t) : []);
            });

        return Diversify(candidates, limit, RecommendDiversity);
    }

    /// <summary>
    /// Builds "Because you watched" rows from the profile's most recent positive signals.
    /// </summary>
    /// <param name="signals">The profile's signals.</param>
    /// <param name="exclude">Titles not to recommend.</param>
    /// <param name="rowCount">How many rows (seeds) at most.</param>
    /// <param name="rowLength">How many titles per row.</param>
    /// <param name="minRowLength">Rows with fewer titles than this are dropped.</param>
    /// <returns>The rows, most recent seed first.</returns>
    public IReadOnlyList<SeedRow> BecauseYouWatched(IEnumerable<TasteSignal> signals, IReadOnlySet<Guid> exclude, int rowCount, int rowLength, int minRowLength = 4)
    {
        var seeds = signals
            .Where(s => s.Weight > 0 && _titles.ContainsKey(s.TitleId))
            .OrderByDescending(s => s.LastActivity ?? DateTime.MinValue)
            .Select(s => s.TitleId)
            .Distinct();

        var rows = new List<SeedRow>();
        foreach (var seedId in seeds)
        {
            if (rows.Count >= rowCount)
            {
                break;
            }

            var titles = SimilarTo(seedId, id => exclude.Contains(id) ? null : 1.0, rowLength);
            if (titles.Count >= minRowLength)
            {
                rows.Add(new SeedRow(_titles[seedId], titles));
            }
        }

        return rows;
    }

    /// <summary>
    /// "More Like This" for a title. Titles the profile already watched are kept but ranked lower.
    /// </summary>
    /// <param name="titleId">The title.</param>
    /// <param name="watched">Titles the profile already watched.</param>
    /// <param name="limit">How many to return.</param>
    /// <returns>Similar titles, best first; empty if the title isn't in the catalog.</returns>
    public IReadOnlyList<ScoredTitle> Similar(Guid titleId, IReadOnlySet<Guid> watched, int limit)
        => _titles.ContainsKey(titleId) ? SimilarTo(titleId, id => watched.Contains(id) ? 0.85 : 1.0, limit) : [];

    /// <summary>
    /// How well a title fits the profile's taste, as a Netflix-style "match" percentage.
    /// </summary>
    /// <remarks>
    /// Raw cosine similarities are small and hard to read, so the title is ranked against the whole catalog:
    /// the best fit in the library is 98%, the worst 60%.
    /// </remarks>
    /// <param name="taste">The profile's taste.</param>
    /// <param name="titleId">The title.</param>
    /// <returns>The match percentage, or null without a taste or for an unknown title.</returns>
    public int? Match(SparseVector taste, Guid titleId)
    {
        if (taste.IsEmpty || !_vectors.TryGetValue(titleId, out var vector))
        {
            return null;
        }

        var similarity = taste.Dot(vector);
        var below = 0;
        var equal = 0;
        foreach (var other in _vectors.Values)
        {
            var otherSimilarity = taste.Dot(other);
            if (otherSimilarity < similarity - 1e-9)
            {
                below++;
            }
            else if (otherSimilarity <= similarity + 1e-9)
            {
                equal++;
            }
        }

        var percentile = (below + (0.5 * equal)) / _vectors.Count;
        return 60 + (int)Math.Round(38 * percentile);
    }

    private static SparseVector BuildVector(Title title, Func<string, double> idf)
    {
        var vector = new SparseVector();

        // Per kind, divide by the square root of the count so a title with twenty tags doesn't drown out its genres.
        foreach (var group in title.Features.DistinctBy(f => f.Key).GroupBy(f => f.Kind))
        {
            var damping = Math.Sqrt(group.Count());
            foreach (var feature in group)
            {
                vector.Set(feature.Key, FeatureWeights.Of(feature.Kind) * idf(feature.Key) / damping);
            }
        }

        return vector.Normalized();
    }

    /// <summary>
    /// A stable per-title, per-day value in [0, 1) so cold-start lists vary from day to day but not between page loads.
    /// </summary>
    private double DailyJitter(Guid id)
    {
        var hash = BitConverter.ToUInt32(id.ToByteArray(), 0) ^ (uint)(_now.Date.Ticks / TimeSpan.TicksPerDay * 2654435761L);
        hash ^= hash >> 15;
        hash *= 2246822519u;
        hash ^= hash >> 13;
        return (hash % 10_000) / 10_000.0;
    }

    private double RecencyFactor(DateTime? lastActivity)
    {
        if (lastActivity is null)
        {
            return UndatedRecencyFactor;
        }

        var ageDays = Math.Max(0, (_now - lastActivity.Value).TotalDays);
        return Math.Max(MinRecencyFactor, Math.Pow(0.5, ageDays / TasteHalfLifeDays));
    }

    private double Quality(Title title) => title.CommunityRating.HasValue ? title.CommunityRating.Value / 10.0 : _meanQuality;

    private double Freshness(Title title) => Math.Exp(-Math.Max(0, (_now - title.DateAdded).TotalDays) / FreshnessDecayDays);

    private IReadOnlyList<ScoredTitle> SimilarTo(Guid seedId, Func<Guid, double?> factor, int limit)
    {
        var seed = _vectors[seedId];
        var candidates = new List<ScoredTitle>();
        foreach (var title in _titles.Values)
        {
            if (title.Id.Equals(seedId) || factor(title.Id) is not { } f)
            {
                continue;
            }

            var similarity = seed.Dot(_vectors[title.Id]);
            if (similarity < MinSeedSimilarity)
            {
                continue;
            }

            var score = ((0.85 * similarity) + (0.15 * Quality(title))) * f;
            candidates.Add(new ScoredTitle(title, score, Reasons(seed, title)));
        }

        return Diversify(candidates, limit, SimilarDiversity);
    }

    /// <summary>
    /// The candidate's traits that contribute most to its fit with <paramref name="reference"/>.
    /// </summary>
    private List<string> Reasons(SparseVector reference, Title title)
    {
        var vector = _vectors[title.Id];
        return vector.Values
            .Select(kv => (kv.Key, Contribution: kv.Value * reference[kv.Key]))
            .Where(x => x.Contribution > 0)
            .OrderByDescending(x => x.Contribution)
            .Take(2)
            .Select(x => _featureNames[x.Key])
            .ToList();
    }

    /// <summary>
    /// Maximal marginal relevance: repeatedly take the candidate with the best trade-off between its own score and
    /// how different it is from what was already picked.
    /// </summary>
    private List<ScoredTitle> Diversify(IEnumerable<ScoredTitle> candidates, int limit, double lambda)
    {
        // Only the top of the list can make it in; skip comparing the long tail.
        var pool = candidates.OrderByDescending(c => c.Score).Take(Math.Max(limit * 4, limit)).ToList();
        var picked = new List<ScoredTitle>(Math.Min(limit, pool.Count));
        var maxSimilarity = new double[pool.Count];
        var taken = new bool[pool.Count];

        while (picked.Count < limit && picked.Count < pool.Count)
        {
            var best = -1;
            var bestValue = double.NegativeInfinity;
            for (var i = 0; i < pool.Count; i++)
            {
                if (taken[i])
                {
                    continue;
                }

                var value = (lambda * pool[i].Score) - ((1 - lambda) * maxSimilarity[i]);
                if (value > bestValue)
                {
                    best = i;
                    bestValue = value;
                }
            }

            taken[best] = true;
            picked.Add(pool[best]);
            var chosen = _vectors[pool[best].Title.Id];
            for (var i = 0; i < pool.Count; i++)
            {
                if (!taken[i])
                {
                    maxSimilarity[i] = Math.Max(maxSimilarity[i], chosen.Dot(_vectors[pool[i].Title.Id]));
                }
            }
        }

        return picked;
    }
}
