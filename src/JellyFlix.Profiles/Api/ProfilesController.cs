using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using JellyFlix.Profiles.Api.Models;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace JellyFlix.Profiles.Api;

/// <summary>
/// Household profiles.
/// </summary>
[ApiController]
[Authorize]
[Route("JellyFlix/Profiles")]
[Produces("application/json")]
public class ProfilesController : ControllerBase
{
    private readonly ProfileService _profileService;
    private readonly IAuthorizationContext _authorizationContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfilesController"/> class.
    /// </summary>
    /// <param name="profileService">The profile service.</param>
    /// <param name="authorizationContext">The authorization context.</param>
    public ProfilesController(ProfileService profileService, IAuthorizationContext authorizationContext)
    {
        _profileService = profileService;
        _authorizationContext = authorizationContext;
    }

    /// <summary>
    /// Gets the current user's household and its profiles.
    /// </summary>
    /// <response code="200">Household returned.</response>
    /// <returns>The household.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<HouseholdDto>> GetHousehold()
    {
        return await Run(auth => _profileService.GetHouseholdAsync(auth.UserId)).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds a profile to the household.
    /// </summary>
    /// <param name="request">The new profile.</param>
    /// <response code="200">Profile created.</response>
    /// <returns>The created profile.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ProfileDto>> CreateProfile([FromBody, Required] CreateProfileDto request)
    {
        return await Run(auth => _profileService.CreateProfileAsync(auth.UserId, request)).ConfigureAwait(false);
    }

    /// <summary>
    /// Changes a profile.
    /// </summary>
    /// <param name="profileId">The profile id.</param>
    /// <param name="request">The changes.</param>
    /// <response code="200">Profile updated.</response>
    /// <returns>The updated profile.</returns>
    [HttpPost("{profileId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProfileDto>> UpdateProfile([FromRoute, Required] Guid profileId, [FromBody, Required] UpdateProfileDto request)
    {
        return await Run(auth => _profileService.UpdateProfileAsync(auth.UserId, profileId, request)).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes a profile and its watch history.
    /// </summary>
    /// <param name="profileId">The profile id.</param>
    /// <response code="204">Profile removed.</response>
    /// <returns>No content.</returns>
    [HttpDelete("{profileId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteProfile([FromRoute, Required] Guid profileId)
    {
        var result = await Run(async auth =>
        {
            await _profileService.DeleteProfileAsync(auth.UserId, profileId).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);

        return result.Result ?? NoContent();
    }

    /// <summary>
    /// Switches this device to another profile in the household. The current session is signed out.
    /// </summary>
    /// <param name="profileId">The profile to switch to.</param>
    /// <param name="request">The PIN, when the profile has one.</param>
    /// <response code="200">Switched; the response holds the new session.</response>
    /// <response code="403">Wrong PIN, or the profile is locked after too many wrong PINs.</response>
    /// <returns>The new session.</returns>
    [HttpPost("{profileId}/Switch")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AuthenticationResult>> SwitchProfile([FromRoute, Required] Guid profileId, [FromBody] SwitchProfileDto? request)
    {
        return await Run(auth =>
        {
            if (string.IsNullOrEmpty(auth.DeviceId) || string.IsNullOrEmpty(auth.Client))
            {
                throw new ProfileException(ProfileError.BadRequest, "The request must identify the client device.");
            }

            var context = new SwitchContext(
                auth.DeviceId,
                auth.Device ?? auth.Client,
                auth.Client,
                auth.Version ?? "0.0.0",
                auth.Token,
                HttpContext.Connection.RemoteIpAddress?.ToString());

            return _profileService.SwitchProfileAsync(auth.UserId, profileId, request?.Pin, context);
        }).ConfigureAwait(false);
    }

    private async Task<AuthorizationInfo> GetUserAuthorizationAsync()
    {
        var auth = await _authorizationContext.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);
        if (auth.IsApiKey || auth.User is null)
        {
            throw new ProfileException(ProfileError.Forbidden, "Profiles require a user session.");
        }

        return auth;
    }

    private async Task<ActionResult<T>> Run<T>(Func<AuthorizationInfo, Task<T>> action)
    {
        try
        {
            var auth = await GetUserAuthorizationAsync().ConfigureAwait(false);
            return await action(auth).ConfigureAwait(false);
        }
        catch (ProfileException ex)
        {
            var status = ex.Error switch
            {
                ProfileError.NotFound => StatusCodes.Status404NotFound,
                ProfileError.Forbidden or ProfileError.InvalidPin => StatusCodes.Status403Forbidden,
                ProfileError.PinLocked => StatusCodes.Status429TooManyRequests,
                _ => StatusCodes.Status400BadRequest
            };

            // 401 is avoided on purpose: clients treat it as "signed out" and drop the session.
            return StatusCode(status, new { Error = ex.Error.ToString(), ex.Message });
        }
    }
}
