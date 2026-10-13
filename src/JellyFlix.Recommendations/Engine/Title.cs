using System;
using System.Collections.Generic;

namespace JellyFlix.Recommendations.Engine;

/// <summary>
/// A movie or series as the engine sees it.
/// </summary>
/// <param name="Id">The item id.</param>
/// <param name="Name">The title.</param>
/// <param name="Features">The title's traits.</param>
/// <param name="CommunityRating">The 0-10 community rating, if known.</param>
/// <param name="DateAdded">When the title was added to the library.</param>
public record Title(Guid Id, string Name, IReadOnlyList<Feature> Features, float? CommunityRating, DateTime DateAdded);
