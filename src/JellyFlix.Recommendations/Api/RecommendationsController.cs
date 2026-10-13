using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using JellyFlix.Recommendations.Api.Models;
using JellyFlix.Recommendations.Engine;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace JellyFlix.Recommendations.Api;

/// <summary>
/// Personalized recommendations for the signed-in profile.
/// </summary>
[ApiController]
[Authorize]
[Route("JellyFlix/Recommendations")]
[Produces("application/json")]
public class RecommendationsController : ControllerBase
{
    private const int MaxLimit = 50;

    private static readonly DtoOptions _dtoOptions = new()
    {
        Fields = [ItemFields.Overview, ItemFields.Genres, ItemFields.PrimaryImageAspectRatio, ItemFields.ChildCount],
        ImageTypes = [ImageType.Primary, ImageType.Backdrop, ImageType.Logo],
        ImageTypeLimit = 1,
        EnableUserData = true
    };

    private readonly RecommendationService _recommendationService;
    private readonly IDtoService _dtoService;
    private readonly IAuthorizationContext _authorizationContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecommendationsController"/> class.
    /// </summary>
    /// <param name="recommendationService">The recommendation service.</param>
    /// <param name="dtoService">The DTO service.</param>
    /// <param name="authorizationContext">The authorization context.</param>
    public RecommendationsController(RecommendationService recommendationService, IDtoService dtoService, IAuthorizationContext authorizationContext)
    {
        _recommendationService = recommendationService;
        _dtoService = dtoService;
        _authorizationContext = authorizationContext;
    }

    /// <summary>
    /// Gets "Top Picks" and "Because you watched" rows for the signed-in profile.
    /// </summary>
    /// <param name="limit">How many top picks.</param>
    /// <param name="rows">How many "Because you watched" rows at most.</param>
    /// <param name="rowLength">How many titles per "Because you watched" row.</param>
    /// <response code="200">Recommendations returned.</response>
    /// <returns>The recommendations.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<RecommendationsDto>> GetRecommendations(
        [FromQuery, Range(1, MaxLimit)] int limit = 20,
        [FromQuery, Range(0, 5)] int rows = 2,
        [FromQuery, Range(1, MaxLimit)] int rowLength = 20)
    {
        var user = await GetUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return Forbid();
        }

        var model = _recommendationService.GetModel(user);
        var picks = model.Engine.Recommend(model.Taste, model.Exclude, limit);
        var pickDtos = ToDtos(model, user, picks);
        var seedRows = model.Engine.BecauseYouWatched(model.Signals, model.Exclude, rows, rowLength);

        return new RecommendationsDto
        {
            IsPersonalized = !model.Taste.IsEmpty,
            Recommended = picks
                .Zip(pickDtos)
                .Select(p => new RecommendedItemDto { Item = p.Second, Reasons = p.First.Reasons })
                .ToList(),
            BecauseYouWatched = seedRows
                .Select(r => new SeedRowDto { SeedId = r.Seed.Id, SeedName = r.Seed.Name, Items = ToDtos(model, user, r.Titles) })
                .ToList()
        };
    }

    /// <summary>
    /// Gets titles like the given one, and how well it matches the signed-in profile.
    /// </summary>
    /// <param name="itemId">The movie or series id.</param>
    /// <param name="limit">How many similar titles.</param>
    /// <response code="200">Similar titles returned.</response>
    /// <response code="404">The title isn't a movie or series this profile can see.</response>
    /// <returns>The similar titles.</returns>
    [HttpGet("Similar/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SimilarDto>> GetSimilar([FromRoute, Required] Guid itemId, [FromQuery, Range(1, MaxLimit)] int limit = 12)
    {
        var user = await GetUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return Forbid();
        }

        var model = _recommendationService.GetModel(user);
        if (!model.Items.ContainsKey(itemId))
        {
            return NotFound();
        }

        return new SimilarDto
        {
            Match = model.Engine.Match(model.Taste, itemId),
            Items = ToDtos(model, user, model.Engine.Similar(itemId, model.Watched, limit))
        };
    }

    private async Task<User?> GetUserAsync()
    {
        var auth = await _authorizationContext.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);
        return auth.IsApiKey ? null : auth.User;
    }

    private BaseItemDto[] ToDtos(RecommendationService.ProfileModel model, User user, IReadOnlyList<ScoredTitle> titles)
    {
        var items = titles.Select(t => model.Items[t.Title.Id]).ToList();

        // The catalog was already filtered to what this profile may see
        return _dtoService.GetBaseItemDtos(items, _dtoOptions, user, skipVisibilityCheck: true).ToArray();
    }
}
