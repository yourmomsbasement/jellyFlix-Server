using System;
using System.Collections.Concurrent;

namespace JellyFlix.Profiles.Security;

/// <summary>
/// Locks a profile for a while after too many wrong PINs, so a four-digit PIN can't be brute forced.
/// </summary>
public class PinAttemptLimiter
{
    /// <summary>
    /// The number of wrong PINs allowed before the profile is locked.
    /// </summary>
    public const int MaxFailures = 5;

    private static readonly TimeSpan _lockoutDuration = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<Guid, (int Failures, DateTime LastFailureUtc)> _attempts = new();
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="PinAttemptLimiter"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider.</param>
    public PinAttemptLimiter(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Checks whether a profile is currently locked.
    /// </summary>
    /// <param name="profileId">The profile id.</param>
    /// <returns>Whether PIN attempts are blocked.</returns>
    public bool IsLocked(Guid profileId)
    {
        if (!_attempts.TryGetValue(profileId, out var entry))
        {
            return false;
        }

        if (_timeProvider.GetUtcNow().UtcDateTime - entry.LastFailureUtc > _lockoutDuration)
        {
            _attempts.TryRemove(profileId, out _);
            return false;
        }

        return entry.Failures >= MaxFailures;
    }

    /// <summary>
    /// Records a wrong PIN.
    /// </summary>
    /// <param name="profileId">The profile id.</param>
    public void RecordFailure(Guid profileId)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _attempts.AddOrUpdate(
            profileId,
            _ => (1, now),
            (_, entry) => now - entry.LastFailureUtc > _lockoutDuration ? (1, now) : (entry.Failures + 1, now));
    }

    /// <summary>
    /// Clears failures after a correct PIN.
    /// </summary>
    /// <param name="profileId">The profile id.</param>
    public void Reset(Guid profileId) => _attempts.TryRemove(profileId, out _);
}
