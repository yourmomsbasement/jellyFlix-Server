using System;

namespace JellyFlix.Recommendations.Engine;

/// <summary>
/// Evidence of a profile's taste: a title it watched, favorited or rated.
/// </summary>
/// <param name="TitleId">The movie or series id.</param>
/// <param name="Weight">How strongly this says "more like this" (negative for dislikes).</param>
/// <param name="LastActivity">When the profile last interacted with the title, used to favor recent taste.</param>
public record TasteSignal(Guid TitleId, double Weight, DateTime? LastActivity);
