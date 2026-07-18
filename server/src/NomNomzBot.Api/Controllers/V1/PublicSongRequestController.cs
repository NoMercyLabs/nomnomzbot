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
using Microsoft.AspNetCore.RateLimiting;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The public, JWT-less surface the <c>/sr/{token}</c> song-request page hits (music-sr.md §3.7). Anonymous and
/// per-IP rate-limited; a token is an opaque capability that resolves to one channel's context (the bot exposes no
/// channel list here). Submitting requests is added in the next slice.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/public/sr")]
[AllowAnonymous]
[EnableRateLimiting("api")]
public sealed class PublicSongRequestController(
    ISongRequestPageTokenService pageTokens,
    IMusicService music
) : BaseController
{
    /// <summary>Resolves a public SR-page token to its channel context; 404 on an unknown/disabled token.</summary>
    [HttpGet("{token}")]
    public async Task<IActionResult> GetPage(string token, CancellationToken cancellationToken) =>
        ResultResponse(await pageTokens.ResolveAsync(token, cancellationToken));

    /// <summary>
    /// Resolves a channel NAME to the same page context — the human-shareable <c>/sr/@name</c> link
    /// ("share your song-request page" without handing out an opaque token). 404 for channels that never
    /// set up their SR page.
    /// </summary>
    [HttpGet("by-channel/{channelName}")]
    public async Task<IActionResult> GetPageByChannel(
        string channelName,
        CancellationToken cancellationToken
    ) => ResultResponse(await pageTokens.ResolveByChannelNameAsync(channelName, cancellationToken));

    /// <summary>
    /// Submits a viewer song request through a public SR-page token. 404 on an unknown token, 409 when the channel
    /// is not accepting requests. The requester label is untrusted display text (anonymous page) — defaults to
    /// "Anonymous"; richer provenance/trust scoring rides on the persisted-queue migration (music-sr.md).
    /// </summary>
    [HttpPost("{token}")]
    public async Task<IActionResult> Submit(
        string token,
        [FromBody] SongRequestDto request,
        CancellationToken cancellationToken
    ) =>
        await SubmitCoreAsync(
            await pageTokens.ResolveAsync(token, cancellationToken),
            request,
            cancellationToken
        );

    /// <summary>Submits a viewer song request through the shareable channel-name link (same gates as the token route).</summary>
    [HttpPost("by-channel/{channelName}")]
    public async Task<IActionResult> SubmitByChannel(
        string channelName,
        [FromBody] SongRequestDto request,
        CancellationToken cancellationToken
    ) =>
        await SubmitCoreAsync(
            await pageTokens.ResolveByChannelNameAsync(channelName, cancellationToken),
            request,
            cancellationToken
        );

    private async Task<IActionResult> SubmitCoreAsync(
        Result<SongRequestPageDto> page,
        SongRequestDto request,
        CancellationToken cancellationToken
    )
    {
        if (page.IsFailure)
            return NotFoundResponse(page.ErrorMessage);
        if (!page.Value.IsAcceptingRequests)
            return ConflictResponse("This channel is not accepting song requests right now.");

        Result added = await music.AddToQueueAsync(
            page.Value.BroadcasterId.ToString(),
            request.Query,
            string.IsNullOrWhiteSpace(request.RequestedBy) ? "Anonymous" : request.RequestedBy,
            cancellationToken
        );
        if (added.IsFailure)
            // TRACK_BLOCKED → 409 with its typed reason; provider-less → 503; etc.
            return ResultResponse(added);
        return Ok(new StatusResponseDto<object> { Message = "Song added to the queue." });
    }
}
