using System.Collections.Generic;
using System.Threading.Tasks;

namespace JellyFlix.Profiles.Data;

/// <summary>
/// Persists households.
/// </summary>
public interface IHouseholdStore
{
    /// <summary>
    /// Loads every household.
    /// </summary>
    /// <returns>The stored households.</returns>
    Task<List<Household>> LoadAsync();

    /// <summary>
    /// Replaces every stored household.
    /// </summary>
    /// <param name="households">The households to store.</param>
    /// <returns>A task representing the save.</returns>
    Task SaveAsync(IReadOnlyList<Household> households);
}
