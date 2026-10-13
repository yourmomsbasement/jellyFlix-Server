using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using JellyFlix.Recommendations.Engine;
using JellyFlix.Recommendations.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace JellyFlix.Recommendations;

/// <summary>
/// Builds and caches each profile's recommendation model.
/// </summary>
/// <remarks>
/// A model is rebuilt when the profile's watch history changes or the library changes, and at least every
/// <see cref="MaxAge"/> so freshness keeps moving.
/// </remarks>
public sealed class RecommendationService : IDisposable
{
    /// <summary>
    /// The longest a cached model is used.
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RecommendationService> _logger;
    private readonly ConcurrentDictionary<Guid, ProfileModel> _models = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="RecommendationService"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public RecommendationService(
        ILibraryManager libraryManager,
        IUserDataManager userDataManager,
        TimeProvider timeProvider,
        ILogger<RecommendationService> logger)
    {
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _timeProvider = timeProvider;
        _logger = logger;

        _userDataManager.UserDataSaved += OnUserDataSaved;
        _libraryManager.ItemAdded += OnLibraryChanged;
        _libraryManager.ItemRemoved += OnLibraryChanged;
        _libraryManager.ItemUpdated += OnLibraryChanged;
    }

    /// <summary>
    /// Gets the profile's recommendation model, building it if needed.
    /// </summary>
    /// <param name="user">The profile's user.</param>
    /// <returns>The model.</returns>
    public ProfileModel GetModel(User user)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (_models.TryGetValue(user.Id, out var cached) && now - cached.BuiltAt < MaxAge)
        {
            return cached;
        }

        var model = Build(user, now);
        _models[user.Id] = model;
        return model;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        _libraryManager.ItemAdded -= OnLibraryChanged;
        _libraryManager.ItemRemoved -= OnLibraryChanged;
        _libraryManager.ItemUpdated -= OnLibraryChanged;
    }

    private ProfileModel Build(User user, DateTime now)
    {
        var started = _timeProvider.GetTimestamp();

        // Only what this profile may see: parental controls and library access apply here
        var items = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            Recursive = true,
            IsVirtualItem = false
        });
        var titles = CatalogBuilder.Build(_libraryManager, items);
        var engine = new RecommendationEngine(titles.Values.ToList(), now);
        var (signals, exclude, watched) = TasteCollector.Collect(_libraryManager, _userDataManager, user);
        var taste = engine.BuildTaste(signals);

        _logger.LogDebug(
            "Built recommendations for {User}: {Titles} titles, {Signals} signals in {Elapsed} ms",
            user.Username,
            titles.Count,
            signals.Count,
            _timeProvider.GetElapsedTime(started).TotalMilliseconds);

        return new ProfileModel(engine, taste, signals, exclude, watched, items.ToDictionary(i => i.Id), now);
    }

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e) => _models.TryRemove(e.UserId, out _);

    private void OnLibraryChanged(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is MediaBrowser.Controller.Entities.Movies.Movie or MediaBrowser.Controller.Entities.TV.Series)
        {
            _models.Clear();
        }
    }

    /// <summary>
    /// Everything needed to answer recommendation requests for one profile.
    /// </summary>
    /// <param name="Engine">The engine over the profile's catalog.</param>
    /// <param name="Taste">The profile's taste.</param>
    /// <param name="Signals">The profile's signals.</param>
    /// <param name="Exclude">Titles not to recommend.</param>
    /// <param name="Watched">Titles the profile watched.</param>
    /// <param name="Items">The catalog's library items by id.</param>
    /// <param name="BuiltAt">When the model was built.</param>
    public sealed record ProfileModel(
        RecommendationEngine Engine,
        SparseVector Taste,
        IReadOnlyList<TasteSignal> Signals,
        IReadOnlySet<Guid> Exclude,
        IReadOnlySet<Guid> Watched,
        IReadOnlyDictionary<Guid, BaseItem> Items,
        DateTime BuiltAt);
}
