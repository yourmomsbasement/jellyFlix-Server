using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using JellyFlix.Profiles.Api.Models;
using JellyFlix.Profiles.Configuration;
using JellyFlix.Profiles.Data;
using JellyFlix.Profiles.Security;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace JellyFlix.Profiles.Tests;

public sealed class ProfileServiceTests : IDisposable
{
    private static readonly SwitchContext _device = new("device-1", "Living Room TV", "Jellyfin Web", "13.0.0", "owner-token", "10.0.0.5");

    private readonly Dictionary<Guid, User> _users = [];
    private readonly Dictionary<Guid, UserPolicy> _policies = [];
    private readonly Mock<IUserManager> _userManager = new();
    private readonly Mock<ISessionManager> _sessionManager = new();
    private readonly InMemoryStore _store = new();
    private readonly PluginConfiguration _config = new();
    private readonly User _owner;
    private readonly ProfileService _service;

    public ProfileServiceTests()
    {
        _owner = AddUser("alex", isAdmin: false);

        _userManager.Setup(m => m.GetUserById(It.IsAny<Guid>()))
            .Returns((Guid id) => _users.GetValueOrDefault(id));
        _userManager.Setup(m => m.GetUserByName(It.IsAny<string>()))
            .Returns((string name) => _users.Values.FirstOrDefault(u => u.Username == name));
        _userManager.Setup(m => m.CreateUserAsync(It.IsAny<string>()))
            .ReturnsAsync((string name) => AddUser(name, isAdmin: false));
        _userManager.Setup(m => m.GetUserDto(It.IsAny<User>(), It.IsAny<string?>()))
            .Returns((User u, string? _) => new UserDto { Id = u.Id, Policy = _policies.GetValueOrDefault(u.Id) ?? new UserPolicy() });
        _userManager.Setup(m => m.UpdatePolicyAsync(It.IsAny<Guid>(), It.IsAny<UserPolicy>()))
            .Callback((Guid id, UserPolicy p) => _policies[id] = p)
            .Returns(Task.CompletedTask);
        _userManager.Setup(m => m.DeleteUserAsync(It.IsAny<Guid>()))
            .Callback((Guid id) => _users.Remove(id))
            .Returns(Task.CompletedTask);

        _sessionManager.Setup(m => m.AuthenticateDirect(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync((AuthenticationRequest r) => new AuthenticationResult { AccessToken = "token-for-" + r.UserId });

        _service = new ProfileService(
            _userManager.Object,
            _sessionManager.Object,
            _store,
            new PinAttemptLimiter(TimeProvider.System),
            () => _config,
            NullLogger<ProfileService>.Instance);
    }

    public void Dispose() => _service.Dispose();

    [Fact]
    public async Task GetHousehold_NewUser_SeesHouseholdOfOne()
    {
        var household = await _service.GetHouseholdAsync(_owner.Id);

        var profile = Assert.Single(household.Profiles);
        Assert.Equal(_owner.Id, profile.Id);
        Assert.True(profile.IsOwner);
        Assert.True(household.CanManage);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public async Task CreateProfile_CreatesHiddenNonAdminUserWithUnknownPassword()
    {
        _policies[_owner.Id] = new UserPolicy { IsAdministrator = true, EnableContentDeletion = true, EnableAllFolders = false, EnabledFolders = [Guid.NewGuid()] };

        var profile = await _service.CreateProfileAsync(_owner.Id, new CreateProfileDto { Name = "Sam" });

        Assert.Equal("alex.Sam", _users[profile.Id].Username);
        var policy = _policies[profile.Id];
        Assert.False(policy.IsAdministrator);
        Assert.True(policy.IsHidden);
        Assert.False(policy.EnableContentDeletion);
        Assert.Equal(_policies[_owner.Id].EnabledFolders, policy.EnabledFolders);
        _userManager.Verify(m => m.ChangePassword(profile.Id, It.Is<string>(p => p.Length >= 32)), Times.Once);

        var stored = Assert.Single(_store.Saved);
        Assert.Equal(_owner.Id, stored.OwnerUserId);
        Assert.Equal(2, stored.Profiles.Count);
    }

    [Fact]
    public async Task CreateProfile_Kids_AppliesParentalRating()
    {
        _config.KidsMaxParentalRating = 7;

        var profile = await _service.CreateProfileAsync(_owner.Id, new CreateProfileDto { Name = "Kids", IsKids = true });

        Assert.True(profile.IsKids);
        Assert.Equal(7, _policies[profile.Id].MaxParentalRating);
        Assert.NotEmpty(_policies[profile.Id].BlockUnratedItems);
    }

    [Fact]
    public async Task CreateProfile_DuplicateName_GetsUniqueUsername()
    {
        AddUser("alex.Sam", isAdmin: false);

        var profile = await _service.CreateProfileAsync(_owner.Id, new CreateProfileDto { Name = "Sam" });

        Assert.Equal("alex.Sam.2", _users[profile.Id].Username);
    }

    [Fact]
    public async Task CreateProfile_AtLimit_IsRefused()
    {
        _config.MaxProfilesPerHousehold = 2;
        await _service.CreateProfileAsync(_owner.Id, new CreateProfileDto { Name = "Sam" });

        var ex = await Assert.ThrowsAsync<ProfileException>(() => _service.CreateProfileAsync(_owner.Id, new CreateProfileDto { Name = "Jo" }));

        Assert.Equal(ProfileError.ProfileLimitReached, ex.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("This name is far too long to fit on a card")]
    public async Task CreateProfile_BadName_IsRefused(string name)
    {
        var ex = await Assert.ThrowsAsync<ProfileException>(() => _service.CreateProfileAsync(_owner.Id, new CreateProfileDto { Name = name }));

        Assert.Equal(ProfileError.BadRequest, ex.Error);
    }

    [Fact]
    public async Task CreateProfile_NonAdminOwnersDisabled_IsRefusedAndNothingIsStored()
    {
        _config.AllowNonAdminOwners = false;

        var ex = await Assert.ThrowsAsync<ProfileException>(() => _service.CreateProfileAsync(_owner.Id, new CreateProfileDto { Name = "Sam" }));

        Assert.Equal(ProfileError.Forbidden, ex.Error);
        _userManager.Verify(m => m.CreateUserAsync(It.IsAny<string>()), Times.Never);
        Assert.False((await _service.GetHouseholdAsync(_owner.Id)).CanManage);
    }

    [Fact]
    public async Task CreateProfile_NonAdminOwnersDisabled_AdminMayStillCreate()
    {
        _config.AllowNonAdminOwners = false;
        _owner.SetPermission(PermissionKind.IsAdministrator, true);

        var profile = await _service.CreateProfileAsync(_owner.Id, new CreateProfileDto { Name = "Sam" });

        Assert.False(profile.IsOwner);
    }

    [Fact]
    public async Task Profile_SeesHouseholdButCannotManageIt()
    {
        var sam = await _service.CreateProfileAsync(_owner.Id, new CreateProfileDto { Name = "Sam" });

        var household = await _service.GetHouseholdAsync(sam.Id);

        Assert.Equal(_owner.Id, household.OwnerId);
        Assert.Equal(2, household.Profiles.Count);
        Assert.True(household.Profiles[0].IsOwner);
        Assert.False(household.CanManage);
        var ex = await Assert.ThrowsAsync<ProfileException>(() => _service.CreateProfileAsync(sam.Id, new CreateProfileDto { Name = "Jo" }));
        Assert.Equal(ProfileError.Forbidden, ex.Error);
        ex = await Assert.ThrowsAsync<ProfileException>(() => _service.DeleteProfileAsync(sam.Id, _owner.Id));
        Assert.Equal(ProfileError.Forbidden, ex.Error);
    }

    [Fact]
    public async Task UpdateProfile_SelfMayRenameButNotChangeKidsSetting()
    {
        var kid = await _service.CreateProfileAsync(_owner.Id, new CreateProfileDto { Name = "Kid", IsKids = true });

        var renamed = await _service.UpdateProfileAsync(kid.Id, kid.Id, new UpdateProfileDto { Name = "Mia", AvatarColor = "#123456" });
        var ex = await Assert.ThrowsAsync<ProfileException>(() => _service.UpdateProfileAsync(kid.Id, kid.Id, new UpdateProfileDto { IsKids = false }));

        Assert.Equal("Mia", renamed.Name);
        Assert.Equal("#123456", renamed.AvatarColor);
        Assert.Equal(ProfileError.Forbidden, ex.Error);
    }

    [Fact]
    public async Task UpdateProfile_OwnerCanSetOwnPinBeforeAddingProfiles()
    {
        var updated = await _service.UpdateProfileAsync(_owner.Id, _owner.Id, new UpdateProfileDto { Pin = "1234" });

        Assert.True(updated.HasPin);
        Assert.Single(_store.Saved);
    }

    [Fact]
    public async Task UpdateProfile_OwnerCannotBecomeKids()
    {
        var ex = await Assert.ThrowsAsync<ProfileException>(() => _service.UpdateProfileAsync(_owner.Id, _owner.Id, new UpdateProfileDto { IsKids = true }));

        Assert.Equal(ProfileError.BadRequest, ex.Error);
    }

    [Fact]
    public async Task SwitchProfile_IssuesSessionOnSameDeviceAndSignsOutOldOne()
    {
        var sam = await _service.CreateProfileAsync(_owner.Id, new CreateProfileDto { Name = "Sam" });

        var result = await _service.SwitchProfileAsync(_owner.Id, sam.Id, null, _device);

        Assert.Equal("token-for-" + sam.Id, result.AccessToken);
        _sessionManager.Verify(
            m => m.AuthenticateDirect(It.Is<AuthenticationRequest>(r =>
                r.UserId.Equals(sam.Id) && r.DeviceId == "device-1" && r.App == "Jellyfin Web" && r.DeviceName == "Living Room TV")),
            Times.Once);
        _sessionManager.Verify(m => m.Logout("owner-token"), Times.Once);
    }

    [Fact]
    public async Task SwitchProfile_RequiresPin()
    {
        await _service.UpdateProfileAsync(_owner.Id, _owner.Id, new UpdateProfileDto { Pin = "1234" });
        var sam = await _service.CreateProfileAsync(_owner.Id, new CreateProfileDto { Name = "Sam" });

        var missing = await Assert.ThrowsAsync<ProfileException>(() => _service.SwitchProfileAsync(sam.Id, _owner.Id, null, _device));
        var wrong = await Assert.ThrowsAsync<ProfileException>(() => _service.SwitchProfileAsync(sam.Id, _owner.Id, "9999", _device));
        await _service.SwitchProfileAsync(sam.Id, _owner.Id, "1234", _device);

        Assert.Equal(ProfileError.InvalidPin, missing.Error);
        Assert.Equal(ProfileError.InvalidPin, wrong.Error);
        _sessionManager.Verify(m => m.AuthenticateDirect(It.IsAny<AuthenticationRequest>()), Times.Once);
    }

    [Fact]
    public async Task SwitchProfile_LocksAfterRepeatedWrongPins()
    {
        await _service.UpdateProfileAsync(_owner.Id, _owner.Id, new UpdateProfileDto { Pin = "1234" });
        var sam = await _service.CreateProfileAsync(_owner.Id, new CreateProfileDto { Name = "Sam" });

        for (var i = 0; i < PinAttemptLimiter.MaxFailures; i++)
        {
            await Assert.ThrowsAsync<ProfileException>(() => _service.SwitchProfileAsync(sam.Id, _owner.Id, "0000", _device));
        }

        var ex = await Assert.ThrowsAsync<ProfileException>(() => _service.SwitchProfileAsync(sam.Id, _owner.Id, "1234", _device));

        Assert.Equal(ProfileError.PinLocked, ex.Error);
        _sessionManager.Verify(m => m.AuthenticateDirect(It.IsAny<AuthenticationRequest>()), Times.Never);
    }

    [Fact]
    public async Task SwitchProfile_OutsideHousehold_IsNotFound()
    {
        var stranger = AddUser("stranger", isAdmin: true);

        var ex = await Assert.ThrowsAsync<ProfileException>(() => _service.SwitchProfileAsync(_owner.Id, stranger.Id, null, _device));

        Assert.Equal(ProfileError.NotFound, ex.Error);
        _sessionManager.Verify(m => m.AuthenticateDirect(It.IsAny<AuthenticationRequest>()), Times.Never);
    }

    [Fact]
    public async Task DeleteProfile_RemovesEntryAndUser()
    {
        var sam = await _service.CreateProfileAsync(_owner.Id, new CreateProfileDto { Name = "Sam" });

        await _service.DeleteProfileAsync(_owner.Id, sam.Id);

        Assert.Single((await _service.GetHouseholdAsync(_owner.Id)).Profiles);
        Assert.False(_users.ContainsKey(sam.Id));
    }

    [Fact]
    public async Task DeleteProfile_OwnerProfile_IsRefused()
    {
        await _service.CreateProfileAsync(_owner.Id, new CreateProfileDto { Name = "Sam" });

        var ex = await Assert.ThrowsAsync<ProfileException>(() => _service.DeleteProfileAsync(_owner.Id, _owner.Id));

        Assert.Equal(ProfileError.BadRequest, ex.Error);
    }

    [Fact]
    public async Task OnUserDeleted_Owner_DissolvesHousehold()
    {
        var sam = await _service.CreateProfileAsync(_owner.Id, new CreateProfileDto { Name = "Sam" });

        await _service.OnUserDeletedAsync(_owner.Id);

        Assert.Empty(_store.Saved);
        var household = await _service.GetHouseholdAsync(sam.Id);
        Assert.Equal(sam.Id, household.OwnerId);
    }

    [Fact]
    public async Task OnUserDeleted_Profile_RemovesIt()
    {
        var sam = await _service.CreateProfileAsync(_owner.Id, new CreateProfileDto { Name = "Sam" });

        await _service.OnUserDeletedAsync(sam.Id);

        Assert.Single(Assert.Single(_store.Saved).Profiles);
    }

    private User AddUser(string name, bool isAdmin)
    {
        var user = new User(name, "auth", "reset") { Id = Guid.NewGuid() };
        user.AddDefaultPermissions();
        user.SetPermission(PermissionKind.IsAdministrator, isAdmin);
        _users[user.Id] = user;
        return user;
    }

    private sealed class InMemoryStore : IHouseholdStore
    {
        public List<Household> Saved { get; private set; } = [];

        public Task<List<Household>> LoadAsync() => Task.FromResult(new List<Household>());

        public Task SaveAsync(IReadOnlyList<Household> households)
        {
            Saved = households.ToList();
            return Task.CompletedTask;
        }
    }
}
