using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Extensions;
using JellyFlix.Recommendations.Engine;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace JellyFlix.Recommendations.Library;

/// <summary>
/// Reads a profile's history into taste signals.
/// </summary>
public static class TasteCollector
{
    /// <summary>
    /// How far back episode history is read. Taste fades with age anyway, so older episodes add little.
    /// </summary>
    public const int MaxEpisodes = 500;

    /// <summary>A watched movie.</summary>
    public const double WatchedWeight = 1.0;

    /// <summary>A movie the profile started but hasn't finished.</summary>
    public const double InProgressWeight = 0.5;

    /// <summary>Extra weight for a favorite (on "My List").</summary>
    public const double FavoriteBonus = 1.0;

    /// <summary>Extra weight for a thumbs-up.</summary>
    public const double LikeBonus = 0.5;

    /// <summary>A thumbs-down replaces every other signal for the title.</summary>
    public const double DislikeWeight = -1.5;

    /// <summary>
    /// Collects signals and the titles that shouldn't be recommended again.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="user">The profile's user.</param>
    /// <returns>The signals, the titles to exclude from recommendations, and the titles the profile has watched.</returns>
    public static (List<TasteSignal> Signals, HashSet<Guid> Exclude, HashSet<Guid> Watched) Collect(
        ILibraryManager libraryManager,
        IUserDataManager userDataManager,
        User user)
    {
        var builders = new Dictionary<Guid, SignalBuilder>();
        SignalBuilder For(Guid id) => builders.TryGetValue(id, out var b) ? b : builders[id] = new SignalBuilder();

        // Movies: watched and in progress
        foreach (var movie in Query(libraryManager, user, BaseItemKind.Movie, q => q.IsPlayed = true))
        {
            For(movie.Id).Watch(WatchedWeight, userDataManager.GetUserData(user, movie));
        }

        foreach (var movie in Query(libraryManager, user, BaseItemKind.Movie, q => q.IsResumable = true))
        {
            For(movie.Id).Watch(InProgressWeight, userDataManager.GetUserData(user, movie));
        }

        // Series: from their most recently watched episodes
        var episodes = Query(libraryManager, user, BaseItemKind.Episode, q =>
        {
            q.IsPlayed = true;
            q.OrderBy = [(ItemSortBy.DatePlayed, SortOrder.Descending)];
            q.Limit = MaxEpisodes;
        });
        var inProgressEpisodes = Query(libraryManager, user, BaseItemKind.Episode, q => q.IsResumable = true);
        foreach (var episode in episodes.Concat(inProgressEpisodes).OfType<Episode>().Where(e => !e.SeriesId.IsEmpty()))
        {
            For(episode.SeriesId).Episode(userDataManager.GetUserData(user, episode));
        }

        // Favorites (My List), whether or not they were watched
        foreach (var item in Query(libraryManager, user, [BaseItemKind.Movie, BaseItemKind.Series], q => q.IsFavorite = true))
        {
            For(item.Id).IsFavorite = true;
        }

        // Thumbs up/down are stored on the movie or series itself
        var signals = new List<TasteSignal>();
        var exclude = new HashSet<Guid>();
        var watched = new HashSet<Guid>();
        foreach (var (id, builder) in builders)
        {
            var item = libraryManager.GetItemById(id);
            if (item is not null)
            {
                builder.Likes = userDataManager.GetUserData(user, item)?.Likes;
            }

            signals.Add(new TasteSignal(id, builder.Weight, builder.LastActivity));
            exclude.Add(id);
            if (builder.HasWatched)
            {
                watched.Add(id);
            }
        }

        return (signals, exclude, watched);
    }

    private static IReadOnlyList<BaseItem> Query(ILibraryManager libraryManager, User user, BaseItemKind kind, Action<InternalItemsQuery> configure)
        => Query(libraryManager, user, [kind], configure);

    private static IReadOnlyList<BaseItem> Query(ILibraryManager libraryManager, User user, BaseItemKind[] kinds, Action<InternalItemsQuery> configure)
    {
        var query = new InternalItemsQuery(user)
        {
            IncludeItemTypes = kinds,
            Recursive = true,
            IsVirtualItem = false
        };
        configure(query);
        return libraryManager.GetItemList(query);
    }

    private sealed class SignalBuilder
    {
        private double _watchWeight;
        private int _episodes;

        public bool IsFavorite { get; set; }

        public bool? Likes { get; set; }

        public DateTime? LastActivity { get; private set; }

        public bool HasWatched => _watchWeight > 0 || _episodes > 0;

        /// <summary>
        /// Gets the final weight. A series counts for more the further into it the profile got, up to five episodes.
        /// </summary>
        public double Weight
        {
            get
            {
                if (Likes == false)
                {
                    return DislikeWeight;
                }

                var weight = _watchWeight;
                if (_episodes > 0)
                {
                    weight = Math.Max(weight, 0.5 + (0.5 * Math.Min(1.0, _episodes / 5.0)));
                }

                if (IsFavorite)
                {
                    weight += FavoriteBonus;
                }

                if (Likes == true)
                {
                    weight += LikeBonus;
                }

                return weight;
            }
        }

        public void Watch(double weight, UserItemData? data)
        {
            _watchWeight = Math.Max(_watchWeight, weight);
            Touch(data);
        }

        public void Episode(UserItemData? data)
        {
            _episodes++;
            Touch(data);
        }

        private void Touch(UserItemData? data)
        {
            if (data?.LastPlayedDate is { } date && (LastActivity is null || date > LastActivity))
            {
                LastActivity = date;
            }
        }
    }
}
