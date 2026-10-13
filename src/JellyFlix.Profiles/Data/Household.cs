using System;
using System.Collections.Generic;

namespace JellyFlix.Profiles.Data;

/// <summary>
/// A household: one owner account and the profiles that belong to it.
/// </summary>
public class Household
{
    /// <summary>
    /// Gets or sets the id of the Jellyfin user that owns the household.
    /// </summary>
    public Guid OwnerUserId { get; set; }

    /// <summary>
    /// Gets or sets the profiles, including the owner's own profile.
    /// </summary>
    public List<ProfileEntry> Profiles { get; set; } = [];
}
