using System;
using System.Collections.Generic;
using System.Linq;
using JellyFlix.Recommendations.Engine;
using Xunit;

namespace JellyFlix.Recommendations.Tests;

public class RecommendationEngineTests
{
    private static readonly DateTime _now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
    private static readonly HashSet<Guid> _none = [];

    private readonly List<Title> _catalog = [];
    private readonly Dictionary<string, Guid> _ids = [];

    public RecommendationEngineTests()
    {
        // A small library with clear clusters
        for (var i = 1; i <= 4; i++)
        {
            Add($"Thriller {i}", 7.5f, 200, (FeatureKind.Genre, "Thriller"), (FeatureKind.Director, "Ann Vale"), (FeatureKind.Actor, "Max Rhee"));
        }

        for (var i = 1; i <= 5; i++)
        {
            Add($"Comedy {i}", 7.0f, 200, (FeatureKind.Genre, "Comedy"), (FeatureKind.Actor, "Jo Park"), (FeatureKind.Studio, "Sunny"));
        }

        for (var i = 1; i <= 3; i++)
        {
            Add($"Cartoon {i}", 6.5f, 200, (FeatureKind.Genre, "Animation"), (FeatureKind.Genre, "Family"));
        }
    }

    [Fact]
    public void Recommend_FavorsTitlesLikeWhatWasWatched()
    {
        var engine = Engine();
        var taste = engine.BuildTaste([Watched("Thriller 1", daysAgo: 2)]);

        var picks = engine.Recommend(taste, Ids("Thriller 1"), 3);

        Assert.All(picks, p => Assert.StartsWith("Thriller", p.Title.Name, StringComparison.Ordinal));
        Assert.DoesNotContain(picks, p => p.Title.Name == "Thriller 1");
    }

    [Fact]
    public void Recommend_ExplainsPicksWithSharedTraits()
    {
        var engine = Engine();
        var taste = engine.BuildTaste([Watched("Thriller 1", daysAgo: 2)]);

        var top = engine.Recommend(taste, Ids("Thriller 1"), 1).Single();

        Assert.NotEmpty(top.Reasons);
        Assert.Subset(new HashSet<string> { "Thriller", "Ann Vale", "Max Rhee" }, top.Reasons.ToHashSet());
    }

    [Fact]
    public void Recommend_DislikesPullAway()
    {
        var engine = Engine();
        var taste = engine.BuildTaste([Watched("Comedy 1", daysAgo: 5), new TasteSignal(_ids["Thriller 1"], -1.5, _now.AddDays(-1))]);

        var picks = engine.Recommend(taste, Ids("Comedy 1", "Thriller 1"), 3);

        Assert.DoesNotContain(picks, p => p.Title.Name.StartsWith("Thriller", StringComparison.Ordinal));
    }

    [Fact]
    public void Recommend_RecentTasteOutweighsOldTaste()
    {
        var engine = Engine();
        var taste = engine.BuildTaste([Watched("Comedy 1", daysAgo: 600), Watched("Thriller 1", daysAgo: 1)]);

        var top = engine.Recommend(taste, Ids("Comedy 1", "Thriller 1"), 1).Single();

        Assert.StartsWith("Thriller", top.Title.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void Recommend_MixesClustersWhenTasteSpansThem()
    {
        var engine = Engine();
        var taste = engine.BuildTaste([Watched("Comedy 1", daysAgo: 1), Watched("Thriller 1", daysAgo: 1)]);

        var picks = engine.Recommend(taste, Ids("Comedy 1", "Thriller 1"), 4);

        Assert.Contains(picks, p => p.Title.Name.StartsWith("Thriller", StringComparison.Ordinal));
        Assert.Contains(picks, p => p.Title.Name.StartsWith("Comedy", StringComparison.Ordinal));
    }

    [Fact]
    public void Recommend_WithoutHistory_RanksByRatingAndFreshness()
    {
        Add("Fresh Hit", 9.0f, 1, (FeatureKind.Genre, "Drama"));
        Add("Old Flop", 3.0f, 900, (FeatureKind.Genre, "Drama"));
        var engine = Engine();

        var taste = engine.BuildTaste([]);
        var picks = engine.Recommend(taste, _none, _catalog.Count);

        Assert.True(taste.IsEmpty);
        Assert.Equal("Fresh Hit", picks[0].Title.Name);
        Assert.Equal("Old Flop", picks[^1].Title.Name);
        Assert.All(picks, p => Assert.Empty(p.Reasons));
    }

    [Fact]
    public void BuildTaste_OnlyDislikes_IsEmpty()
    {
        var engine = Engine();

        var taste = engine.BuildTaste([new TasteSignal(_ids["Thriller 1"], -1.5, _now)]);

        Assert.True(taste.IsEmpty);
    }

    [Fact]
    public void RareSharedTraits_CountMoreThanCommonOnes()
    {
        // "Drama" is everywhere; "Lena Ortiz" directed just two titles.
        for (var i = 1; i <= 8; i++)
        {
            Add($"Drama {i}", 7f, 200, (FeatureKind.Genre, "Drama"));
        }

        Add("Ortiz A", 7f, 200, (FeatureKind.Genre, "Drama"), (FeatureKind.Director, "Lena Ortiz"));
        Add("Ortiz B", 7f, 200, (FeatureKind.Genre, "War"), (FeatureKind.Director, "Lena Ortiz"));
        var engine = Engine();

        var similar = engine.Similar(_ids["Ortiz A"], _none, 3);

        Assert.Equal("Ortiz B", similar[0].Title.Name);
    }

    [Fact]
    public void BecauseYouWatched_UsesMostRecentSeedsAndSkipsThinRows()
    {
        Add("Lonely Doc", 7f, 200, (FeatureKind.Genre, "Documentary"));
        var engine = Engine();
        var signals = new[] { Watched("Comedy 1", daysAgo: 10), Watched("Lonely Doc", daysAgo: 1), Watched("Thriller 1", daysAgo: 3) };

        var rows = engine.BecauseYouWatched(signals, Ids("Comedy 1", "Lonely Doc", "Thriller 1"), rowCount: 2, rowLength: 10, minRowLength: 2);

        // "Lonely Doc" is most recent but nothing is like it, so the next seeds are used instead
        Assert.Equal(["Thriller 1", "Comedy 1"], rows.Select(r => r.Seed.Name));
        Assert.All(rows[0].Titles, t => Assert.StartsWith("Thriller", t.Title.Name, StringComparison.Ordinal));
        Assert.DoesNotContain(rows.SelectMany(r => r.Titles), t => t.Title.Name is "Thriller 1" or "Comedy 1");
    }

    [Fact]
    public void Similar_ExcludesItselfAndRanksWatchedLower()
    {
        var engine = Engine();

        var similar = engine.Similar(_ids["Thriller 1"], Ids("Thriller 2"), 3);

        Assert.DoesNotContain(similar, s => s.Title.Name == "Thriller 1");
        Assert.Contains(similar, s => s.Title.Name == "Thriller 2");
        Assert.NotEqual("Thriller 2", similar[0].Title.Name);
    }

    [Fact]
    public void Similar_UnknownTitle_IsEmpty()
    {
        Assert.Empty(Engine().Similar(Guid.NewGuid(), _none, 5));
    }

    [Fact]
    public void Match_IsHighForBestFitAndLowForUnrelated()
    {
        var engine = Engine();
        var taste = engine.BuildTaste([Watched("Thriller 1", daysAgo: 1)]);

        var good = engine.Match(taste, _ids["Thriller 2"]);
        var bad = engine.Match(taste, _ids["Cartoon 1"]);

        Assert.InRange(good!.Value, 85, 98);
        Assert.InRange(bad!.Value, 60, 75);
        Assert.Null(engine.Match(engine.BuildTaste([]), _ids["Thriller 2"]));
    }

    private RecommendationEngine Engine() => new(_catalog, _now);

    private TasteSignal Watched(string name, int daysAgo) => new(_ids[name], 1.0, _now.AddDays(-daysAgo));

    private HashSet<Guid> Ids(params string[] names) => names.Select(n => _ids[n]).ToHashSet();

    private void Add(string name, float rating, int daysSinceAdded, params (FeatureKind Kind, string Name)[] features)
    {
        var id = Guid.NewGuid();
        _ids[name] = id;
        _catalog.Add(new Title(id, name, features.Select(f => new Feature(f.Kind, f.Name)).ToList(), rating, _now.AddDays(-daysSinceAdded)));
    }
}
