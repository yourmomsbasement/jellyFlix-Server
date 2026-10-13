using System;
using System.Collections.Generic;

namespace JellyFlix.Profiles.Api.Models;

/// <summary>
/// The household the current user belongs to.
/// </summary>
public class HouseholdDto
{
    /// <summary>
    /// Gets or sets the household owner's user id.
    /// </summary>
    public Guid OwnerId { get; set; }

    /// <summary>
    /// Gets or sets the id of the profile making the request.
    /// </summary>
    public Guid CurrentProfileId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the current profile may add, edit and remove profiles.
    /// </summary>
    public bool CanManage { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of profiles allowed in the household.
    /// </summary>
    public int MaxProfiles { get; set; }

    /// <summary>
    /// Gets or sets the profiles, owner first.
    /// </summary>
    public IReadOnlyList<ProfileDto> Profiles { get; set; } = [];
}
