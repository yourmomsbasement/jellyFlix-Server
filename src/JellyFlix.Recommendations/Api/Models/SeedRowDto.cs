using System;
using System.Collections.Generic;
using MediaBrowser.Model.Dto;

namespace JellyFlix.Recommendations.Api.Models;

/// <summary>
/// A "Because you watched {SeedName}" row.
/// </summary>
public class SeedRowDto
{
    /// <summary>
    /// Gets or sets the id of the title the row is based on.
    /// </summary>
    public Guid SeedId { get; set; }

    /// <summary>
    /// Gets or sets the name of the title the row is based on.
    /// </summary>
    public string SeedName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the similar titles.
    /// </summary>
    public IReadOnlyList<BaseItemDto> Items { get; set; } = [];
}
