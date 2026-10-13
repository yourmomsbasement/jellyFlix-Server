using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Extensions.Json;
using JellyFlix.Profiles.Api.Models;
using JellyFlix.Profiles.Configuration;
using JellyFlix.Profiles.Data;
using JellyFlix.Profiles.Security;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Users;
using Microsoft.Extensions.Logging;

namespace JellyFlix.Profiles;

/// <summary>
/// Manages households and switching between their profiles.
/// </summary>
public sealed partial class ProfileService : IDisposable
{
    /// <summary>
    /// The longest allowed profile name.
    /// </summary>
    public const int MaxNameLength = 32;

    private static readonly string[] _avatarColors = ["#E50914", "#0071EB", "#F5C518", "#2BB673", "#8E44AD", "#FF7A00"];

    private readonly IUserManager _userManager;
    private readonly ISessionManager _sessionManager;
    private readonly IHouseholdStore _store;
    private readonly PinAttemptLimiter _pinLimiter;
    private readonly Func<PluginConfiguration> _configuration;
    private readonly ILogger<ProfileService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private List<Household>? _households;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileService"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="store">The household store.</param>
    /// <param name="pinLimiter">The PIN attempt limiter.</param>
    /// <param name="configuration">Returns the current plugin configuration.</param>
    /// <param name="logger">The logger.</param>
    public ProfileService(
        IUserManager userManager,
        ISessionManager sessionManager,
        IHouseholdStore store,
        PinAttemptLimiter pinLimiter,
        Func<PluginConfiguration> configuration,
        ILogger<ProfileService> logger)
    {
        _userManager = userManager;
        _sessionManager = sessionManager;
        _store = store;
        _pinLimiter = pinLimiter;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Gets the household of the calling user. A user that isn't in a household yet sees a household of one.
    /// </summary>
    /// <param name="callerId">The calling user id.</param>
    /// <returns>The household.</returns>
    public async Task<HouseholdDto> GetHouseholdAsync(Guid callerId)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var households = await GetHouseholdsAsync().ConfigureAwait(false);
            var household = FindByMember(households, callerId) ?? CreateImplicitHousehold(GetUser(callerId));
            return ToHouseholdDto(household, callerId);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Adds a profile to the caller's household. Only the household owner may do this.
    /// </summary>
    /// <param name="callerId">The calling user id.</param>
    /// <param name="request">The new profile.</param>
    /// <returns>The created profile.</returns>
    public async Task<ProfileDto> CreateProfileAsync(Guid callerId, CreateProfileDto request)
    {
        var name = ValidateName(request.Name);
        ValidateColor(request.AvatarColor);
        if (request.Pin is not null && !PinHasher.IsValidPin(request.Pin))
        {
            throw new ProfileException(ProfileError.BadRequest, "PIN must be four digits.");
        }

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var households = await GetHouseholdsAsync().ConfigureAwait(false);
            var owner = GetUser(callerId);
            var household = FindByMember(households, callerId);
            var isNewHousehold = household is null;
            household ??= CreateImplicitHousehold(owner);
            EnsureCanManage(household, callerId);

            if (household.Profiles.Count >= _configuration().MaxProfilesPerHousehold)
            {
                throw new ProfileException(ProfileError.ProfileLimitReached, "This household already has the maximum number of profiles.");
            }

            var user = await _userManager.CreateUserAsync(GetAvailableUsername(owner.Username, name)).ConfigureAwait(false);
            try
            {
                // Profiles sign in only through the household, so give the backing user a password nobody knows.
                await _userManager.ChangePassword(user.Id, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))).ConfigureAwait(false);
                await ApplyPolicyAsync(user.Id, owner, request.IsKids).ConfigureAwait(false);
            }
            catch
            {
                await _userManager.DeleteUserAsync(user.Id).ConfigureAwait(false);
                throw;
            }

            var entry = new ProfileEntry
            {
                UserId = user.Id,
                DisplayName = name,
                AvatarColor = request.AvatarColor ?? _avatarColors[household.Profiles.Count % _avatarColors.Length],
                PinHash = request.Pin is null ? null : PinHasher.Hash(request.Pin),
                IsKids = request.IsKids
            };
            household.Profiles.Add(entry);
            if (isNewHousehold)
            {
                households.Add(household);
            }

            await SaveAsync().ConfigureAwait(false);

            _logger.LogInformation("Created profile {ProfileName} ({UserId}) in household of {Owner}", name, user.Id, owner.Username);
            return ToProfileDto(household, entry);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Changes a profile. The owner may change any profile; other profiles may change their own name, avatar and PIN.
    /// </summary>
    /// <param name="callerId">The calling user id.</param>
    /// <param name="profileId">The profile to change.</param>
    /// <param name="request">The changes.</param>
    /// <returns>The updated profile.</returns>
    public async Task<ProfileDto> UpdateProfileAsync(Guid callerId, Guid profileId, UpdateProfileDto request)
    {
        var name = request.Name is null ? null : ValidateName(request.Name);
        ValidateColor(request.AvatarColor);
        if (request.Pin is not null && !PinHasher.IsValidPin(request.Pin))
        {
            throw new ProfileException(ProfileError.BadRequest, "PIN must be four digits.");
        }

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var households = await GetHouseholdsAsync().ConfigureAwait(false);
            var household = FindByMember(households, callerId);
            if (household is null)
            {
                // The owner editing their own profile before adding any others.
                if (!profileId.Equals(callerId))
                {
                    throw new ProfileException(ProfileError.NotFound, "Profile not found.");
                }

                household = CreateImplicitHousehold(GetUser(callerId));
                households.Add(household);
            }

            var entry = FindProfile(household, profileId);
            var isManager = CanManage(household, callerId);
            if (!isManager && !profileId.Equals(callerId))
            {
                throw new ProfileException(ProfileError.Forbidden, "Only the household owner can change other profiles.");
            }

            if (request.IsKids.HasValue && request.IsKids.Value != entry.IsKids)
            {
                if (!isManager)
                {
                    throw new ProfileException(ProfileError.Forbidden, "Only the household owner can change kids settings.");
                }

                if (profileId.Equals(household.OwnerUserId))
                {
                    throw new ProfileException(ProfileError.BadRequest, "The owner profile can't be a kids profile.");
                }

                await ApplyPolicyAsync(profileId, GetUser(household.OwnerUserId), request.IsKids.Value).ConfigureAwait(false);
                entry.IsKids = request.IsKids.Value;
            }

            if (name is not null)
            {
                entry.DisplayName = name;
            }

            if (request.AvatarColor is not null)
            {
                entry.AvatarColor = request.AvatarColor;
            }

            if (request.RemovePin)
            {
                entry.PinHash = null;
            }
            else if (request.Pin is not null)
            {
                entry.PinHash = PinHasher.Hash(request.Pin);
            }

            await SaveAsync().ConfigureAwait(false);
            return ToProfileDto(household, entry);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Removes a profile and the Jellyfin user behind it, including its watch history. Only the owner may do this.
    /// </summary>
    /// <param name="callerId">The calling user id.</param>
    /// <param name="profileId">The profile to remove.</param>
    /// <returns>A task representing the removal.</returns>
    public async Task DeleteProfileAsync(Guid callerId, Guid profileId)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var households = await GetHouseholdsAsync().ConfigureAwait(false);
            var household = FindByMember(households, callerId)
                ?? throw new ProfileException(ProfileError.NotFound, "Profile not found.");
            var entry = FindProfile(household, profileId);
            EnsureCanManage(household, callerId);

            if (profileId.Equals(household.OwnerUserId))
            {
                throw new ProfileException(ProfileError.BadRequest, "The owner profile can't be removed.");
            }

            household.Profiles.Remove(entry);
            await SaveAsync().ConfigureAwait(false);
            _pinLimiter.Reset(profileId);
        }
        finally
        {
            _lock.Release();
        }

        // Outside the lock: deleting the user raises a user-deleted event that calls back into this service.
        await _userManager.DeleteUserAsync(profileId).ConfigureAwait(false);
    }

    /// <summary>
    /// Switches the calling device to another profile in the same household and returns that profile's session.
    /// </summary>
    /// <param name="callerId">The calling user id.</param>
    /// <param name="profileId">The profile to switch to.</param>
    /// <param name="pin">The target profile's PIN, if it has one.</param>
    /// <param name="context">The calling device.</param>
    /// <returns>The new session.</returns>
    public async Task<AuthenticationResult> SwitchProfileAsync(Guid callerId, Guid profileId, string? pin, SwitchContext context)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var households = await GetHouseholdsAsync().ConfigureAwait(false);
            var household = FindByMember(households, callerId) ?? CreateImplicitHousehold(GetUser(callerId));
            var entry = FindProfile(household, profileId);

            if (entry.PinHash is not null && !profileId.Equals(callerId))
            {
                if (_pinLimiter.IsLocked(profileId))
                {
                    throw new ProfileException(ProfileError.PinLocked, "Too many wrong PINs. Try again later.");
                }

                if (!PinHasher.Verify(pin, entry.PinHash))
                {
                    _pinLimiter.RecordFailure(profileId);
                    throw new ProfileException(ProfileError.InvalidPin, "Incorrect PIN.");
                }

                _pinLimiter.Reset(profileId);
            }
        }
        finally
        {
            _lock.Release();
        }

        var user = GetUser(profileId);
        if (user.HasPermission(PermissionKind.IsDisabled))
        {
            throw new ProfileException(ProfileError.Forbidden, "This profile is disabled.");
        }

        var result = await _sessionManager.AuthenticateDirect(new AuthenticationRequest
        {
            UserId = profileId,
            Username = user.Username,
            App = context.Client,
            AppVersion = context.Version,
            DeviceId = context.DeviceId,
            DeviceName = context.DeviceName,
            RemoteEndPoint = context.RemoteEndPoint
        }).ConfigureAwait(false);

        // One profile per device at a time: sign out the session we switched away from.
        if (!profileId.Equals(callerId) && !string.IsNullOrEmpty(context.CurrentToken))
        {
            await _sessionManager.Logout(context.CurrentToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// Keeps households consistent when a user is deleted outside the plugin, e.g. from the dashboard.
    /// </summary>
    /// <param name="userId">The deleted user id.</param>
    /// <returns>A task representing the cleanup.</returns>
    public async Task OnUserDeletedAsync(Guid userId)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var households = await GetHouseholdsAsync().ConfigureAwait(false);
            var household = FindByMember(households, userId);
            if (household is null)
            {
                return;
            }

            if (household.OwnerUserId.Equals(userId))
            {
                households.Remove(household);
                var orphans = household.Profiles.Where(p => !p.UserId.Equals(userId)).Select(p => p.DisplayName);
                _logger.LogWarning(
                    "Household owner {UserId} was deleted. Its profiles are now standalone hidden users and can be removed from the dashboard: {Profiles}",
                    userId,
                    string.Join(", ", orphans));
            }
            else
            {
                household.Profiles.RemoveAll(p => p.UserId.Equals(userId));
            }

            await SaveAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _lock.Dispose();

    private static Household? FindByMember(List<Household> households, Guid userId)
        => households.Find(h => h.Profiles.Exists(p => p.UserId.Equals(userId)));

    private static ProfileEntry FindProfile(Household household, Guid profileId)
        => household.Profiles.Find(p => p.UserId.Equals(profileId))
            ?? throw new ProfileException(ProfileError.NotFound, "Profile not found.");

    private static string ValidateName(string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
        {
            throw new ProfileException(ProfileError.BadRequest, $"Profile name must be 1-{MaxNameLength} characters.");
        }

        return name;
    }

    private static void ValidateColor(string? color)
    {
        if (color is not null && !ColorRegex().IsMatch(color))
        {
            throw new ProfileException(ProfileError.BadRequest, "Avatar color must be #RRGGBB.");
        }
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorRegex();

    [GeneratedRegex(@"[^\w\-'._@+ ]")]
    private static partial Regex InvalidUsernameCharsRegex();

    private static Household CreateImplicitHousehold(User owner) => new()
    {
        OwnerUserId = owner.Id,
        Profiles =
        [
            new ProfileEntry
            {
                UserId = owner.Id,
                DisplayName = owner.Username,
                AvatarColor = _avatarColors[0]
            }
        ]
    };

    private async Task<List<Household>> GetHouseholdsAsync()
        => _households ??= await _store.LoadAsync().ConfigureAwait(false);

    private Task SaveAsync() => _store.SaveAsync(_households!);

    private User GetUser(Guid userId)
        => _userManager.GetUserById(userId) ?? throw new ProfileException(ProfileError.NotFound, "User not found.");

    private bool CanManage(Household household, Guid callerId)
    {
        if (!household.OwnerUserId.Equals(callerId))
        {
            return false;
        }

        return _configuration().AllowNonAdminOwners
            || _userManager.GetUserById(callerId)?.HasPermission(PermissionKind.IsAdministrator) == true;
    }

    private void EnsureCanManage(Household household, Guid callerId)
    {
        if (!CanManage(household, callerId))
        {
            throw new ProfileException(ProfileError.Forbidden, "Only the household owner can manage profiles.");
        }
    }

    private string GetAvailableUsername(string ownerName, string profileName)
    {
        var baseName = InvalidUsernameCharsRegex().Replace($"{ownerName}.{profileName}", string.Empty).Trim();
        var candidate = baseName;
        for (var i = 2; _userManager.GetUserByName(candidate) is not null; i++)
        {
            candidate = string.Create(CultureInfo.InvariantCulture, $"{baseName}.{i}");
        }

        return candidate;
    }

    /// <summary>
    /// Gives a profile user the owner's library access, minus anything administrative, and hides it from the login screen.
    /// </summary>
    private async Task ApplyPolicyAsync(Guid profileUserId, User owner, bool isKids)
    {
        var ownerPolicy = _userManager.GetUserDto(owner).Policy;
        var policy = JsonSerializer.Deserialize<UserPolicy>(JsonSerializer.Serialize(ownerPolicy, JsonDefaults.Options), JsonDefaults.Options)!;

        policy.IsAdministrator = false;
        policy.IsHidden = true;
        policy.IsDisabled = false;
        policy.EnableRemoteControlOfOtherUsers = false;
        policy.EnableSharedDeviceControl = false;
        policy.EnableContentDeletion = false;
        policy.EnableContentDeletionFromFolders = [];
        policy.EnableCollectionManagement = false;
        policy.EnableSubtitleManagement = false;
        policy.EnableLyricManagement = false;
        policy.EnableUserPreferenceAccess = true;

        if (isKids)
        {
            policy.MaxParentalRating = _configuration().KidsMaxParentalRating;
            policy.MaxParentalSubRating = null;
            policy.BlockUnratedItems = Enum.GetValues<UnratedItem>();
        }

        await _userManager.UpdatePolicyAsync(profileUserId, policy).ConfigureAwait(false);
    }

    private HouseholdDto ToHouseholdDto(Household household, Guid callerId) => new()
    {
        OwnerId = household.OwnerUserId,
        CurrentProfileId = callerId,
        CanManage = CanManage(household, callerId),
        MaxProfiles = _configuration().MaxProfilesPerHousehold,
        Profiles = household.Profiles
            .OrderByDescending(p => p.UserId.Equals(household.OwnerUserId))
            .Select(p => ToProfileDto(household, p))
            .ToList()
    };

    private ProfileDto ToProfileDto(Household household, ProfileEntry entry)
    {
        var user = _userManager.GetUserById(entry.UserId);
        return new ProfileDto
        {
            Id = entry.UserId,
            Name = entry.DisplayName,
            AvatarColor = entry.AvatarColor,
            PrimaryImageTag = user is null ? null : _userManager.GetUserDto(user).PrimaryImageTag,
            HasPin = entry.PinHash is not null,
            IsKids = entry.IsKids,
            IsOwner = entry.UserId.Equals(household.OwnerUserId)
        };
    }
}
