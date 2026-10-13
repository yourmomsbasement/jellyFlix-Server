using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using JellyFlix.Recommendations.Engine;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace JellyFlix.Recommendations.Library;

/// <summary>
/// Turns library items into engine <see cref="Title"/>s.
/// </summary>
public static class CatalogBuilder
{
    /// <summary>
    /// Only the top-billed actors say much about a title.
    /// </summary>
    public const int MaxActors = 6;

    /// <summary>
    /// Builds titles for items, fetching everyone's credits in one query.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="items">Movies and series.</param>
    /// <returns>Titles by item id.</returns>
    public static Dictionary<Guid, Title> Build(ILibraryManager libraryManager, IReadOnlyList<BaseItem> items)
    {
        var people = items.Count == 0
            ? new Dictionary<Guid, IReadOnlyList<PersonInfo>>()
            : libraryManager.GetPeopleByItems(items.Select(i => i.Id).ToList());

        return items.ToDictionary(
            item => item.Id,
            item => ToTitle(item, people.TryGetValue(item.Id, out var credits) ? credits : []));
    }

    /// <summary>
    /// Builds a title from an item and its credits.
    /// </summary>
    /// <param name="item">The movie or series.</param>
    /// <param name="credits">The item's people.</param>
    /// <returns>The title.</returns>
    public static Title ToTitle(BaseItem item, IReadOnlyList<PersonInfo> credits)
    {
        var features = new List<Feature>();
        features.AddRange(item.Genres.Select(g => new Feature(FeatureKind.Genre, g)));
        features.AddRange(item.Tags.Select(t => new Feature(FeatureKind.Tag, t)));
        features.AddRange(item.Studios.Select(s => new Feature(FeatureKind.Studio, s)));
        features.AddRange(credits.Where(p => p.IsType(PersonKind.Director)).Select(p => new Feature(FeatureKind.Director, p.Name)));
        features.AddRange(credits.Where(p => p.IsType(PersonKind.Writer)).Select(p => new Feature(FeatureKind.Writer, p.Name)));
        features.AddRange(credits
            .Where(p => p.IsType(PersonKind.Actor) || p.IsType(PersonKind.GuestStar))
            .OrderBy(p => p.SortOrder ?? int.MaxValue)
            .Take(MaxActors)
            .Select(p => new Feature(FeatureKind.Actor, p.Name)));

        if (item.ProductionYear is { } year)
        {
            features.Add(new Feature(FeatureKind.Decade, $"{year / 10 * 10}s"));
        }

        return new Title(
            item.Id,
            item.Name ?? string.Empty,
            features.Where(f => !string.IsNullOrWhiteSpace(f.Name)).ToList(),
            item.CommunityRating,
            item.DateCreated);
    }
}
