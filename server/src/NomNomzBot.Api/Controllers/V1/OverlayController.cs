// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The PUBLIC overlay surface (widgets-overlays.md §5b) — the only widget read a browser source hits without a
/// user session. Auth is the per-channel <c>OverlayToken</c> (a query param), never the user JWT. The manifest lists
/// the channel's live widgets + bundle URLs; the bundle endpoint serves a widget's active compiled bundle, which the
/// overlay host page injects.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/overlay")]
[AllowAnonymous]
[Tags("Overlay")]
public class OverlayController : BaseController
{
    private readonly IWidgetService _widgetService;

    public OverlayController(IWidgetService widgetService)
    {
        _widgetService = widgetService;
    }

    /// <summary>Resolve a channel's overlay manifest by its overlay token.</summary>
    [HttpGet("manifest")]
    [ProducesResponseType<StatusResponseDto<OverlayManifest>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetManifest([FromQuery] string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequestResponse("An overlay token is required.");

        Result<OverlayManifest> result = await _widgetService.GetOverlayManifestAsync(token, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<OverlayManifest> { Data = result.Value });
    }

    /// <summary>
    /// A short-lived scoped Spotify access token for the OBS playback widget's in-browser Web Playback SDK
    /// player. Never the refresh token; the widget re-fetches this whenever the SDK's <c>getOAuthToken</c>
    /// callback fires (roughly hourly, matching Spotify's own token lifetime).
    /// </summary>
    [HttpGet("spotify-token")]
    [ProducesResponseType<StatusResponseDto<string>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSpotifyPlaybackToken(
        [FromQuery] string? token,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequestResponse("An overlay token is required.");

        Result<string> result = await _widgetService.GetSpotifyPlaybackTokenAsync(token, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<string> { Data = result.Value });
    }

    /// <summary>
    /// The current playback state, so a <c>now_playing</c> widget can render the real track the instant it mounts
    /// instead of showing nothing until the next <c>now_playing</c> hub event arrives. <c>Data</c> is null when
    /// nothing is currently playing.
    /// </summary>
    [HttpGet("now-playing")]
    [ProducesResponseType<StatusResponseDto<OverlayNowPlayingSnapshot>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNowPlayingSnapshot(
        [FromQuery] string? token,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequestResponse("An overlay token is required.");

        Result<OverlayNowPlayingSnapshot?> result = await _widgetService.GetNowPlayingSnapshotAsync(
            token,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<OverlayNowPlayingSnapshot?> { Data = result.Value });
    }

    /// <summary>
    /// A single script-storage value for this channel, so a widget can bootstrap durable game/session state
    /// (e.g. the current holder of a hot-potato reward) the instant it mounts instead of waiting for the next
    /// live event.
    /// </summary>
    [HttpGet("storage/{key}")]
    [ProducesResponseType<StatusResponseDto<string>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetScriptStorageValue(
        string key,
        [FromQuery] string? token,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequestResponse("An overlay token is required.");

        Result<string?> result = await _widgetService.GetScriptStorageValueAsync(token, key, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<string?> { Data = result.Value });
    }

    /// <summary>The channel's current playback queue (excluding the now-playing track).</summary>
    [HttpGet("queue")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<MusicQueueItem>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetQueueSnapshot(
        [FromQuery] string? token,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequestResponse("An overlay token is required.");

        Result<IReadOnlyList<MusicQueueItem>> result = await _widgetService.GetQueueSnapshotAsync(
            token,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<IReadOnlyList<MusicQueueItem>> { Data = result.Value });
    }

    /// <summary>
    /// Serve a widget's active compiled bundle. The URL carries the content hash as <c>?v=</c>, so the response is
    /// safely long-cacheable (a new compile / rollback changes the hash → a fresh URL the host fetches instead).
    /// </summary>
    [HttpGet("bundle/{widgetId}")]
    [Produces("text/html", "application/javascript")]
    public async Task<IActionResult> GetBundle(
        string widgetId,
        [FromQuery] string? token,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequestResponse("An overlay token is required.");

        Result<OverlayBundle> result = await _widgetService.GetOverlayBundleAsync(
            token,
            widgetId,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);

        // A vanilla widget's bundle is browser-ready HTML; a framework widget's is a JS module.
        string contentType =
            result.Value.Framework == "vanilla"
                ? "text/html; charset=utf-8"
                : "application/javascript; charset=utf-8";
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        // Tell the overlay host which framework this bundle is: for "vue" it injects the Vue runtime
        // (/overlay/vue.js) into the widget iframe before the bundle, so window.Vue exists when the bundle mounts.
        Response.Headers["X-Widget-Framework"] = result.Value.Framework;
        return Content(result.Value.Content, contentType);
    }
}
