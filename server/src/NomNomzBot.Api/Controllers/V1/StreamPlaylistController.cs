// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NomNomzBot.Api.RateLimiting;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The live playlist proxy behind the dashboard's stream player (TwitchPlugin <c>proxyUrl</c>). The player asks
/// for <c>{base}/{base64url(address)}/index.m3u8</c>; only the playlists pass through here, the video segments
/// load straight from Twitch's CDN.
/// Anonymous on purpose: the player's HLS loader sends no Authorization header, so a signed-in check cannot
/// apply. What protects it instead: the address must be on Twitch's playlist hosts (https only, see
/// <see cref="ITwitchLivePlaylistProxy"/>), so it can never fetch another site, and a per-IP rate limit sized to
/// a player's refresh pace. Left out of the OpenAPI snapshot: only the player calls it, by its own URL form.
/// </summary>
[ApiController]
[Route("api/v1/stream/playlist")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[EnableRateLimiting(LivePlaylistRateLimitPolicy.PolicyName)]
public sealed class StreamPlaylistController(ITwitchLivePlaylistProxy proxy) : ControllerBase
{
    private const string PlaylistContentType = "application/vnd.apple.mpegurl";

    [HttpGet("{encoded}/index.m3u8")]
    public async Task<IActionResult> Playlist(string encoded, CancellationToken ct)
    {
        // A live playlist changes every few seconds: no browser or proxy may keep a copy, nor of a refusal.
        Response.Headers.CacheControl = "no-store";

        Result<string> playlist = await proxy.FetchAsync(encoded, ct);
        if (playlist.IsSuccess)
            return Content(playlist.Value, PlaylistContentType);

        return StatusCode(
            playlist.ErrorCode switch
            {
                ITwitchLivePlaylistProxy.InvalidAddress => StatusCodes.Status400BadRequest,
                TwitchErrorCodes.NotFound => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status502BadGateway,
            }
        );
    }
}
