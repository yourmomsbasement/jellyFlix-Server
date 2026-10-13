using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using MediaBrowser.Controller.Events;

namespace JellyFlix.Profiles.Events;

/// <summary>
/// Removes deleted users from their household.
/// </summary>
public class UserDeletedConsumer : IEventConsumer<UserDeletedEventArgs>
{
    private readonly ProfileService _profileService;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserDeletedConsumer"/> class.
    /// </summary>
    /// <param name="profileService">The profile service.</param>
    public UserDeletedConsumer(ProfileService profileService)
    {
        _profileService = profileService;
    }

    /// <inheritdoc />
    public Task OnEvent(UserDeletedEventArgs eventArgs) => _profileService.OnUserDeletedAsync(eventArgs.Argument.Id);
}
