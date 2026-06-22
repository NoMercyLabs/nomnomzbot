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
using NomNomzBot.Api.Controllers;
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
public sealed class PublicSongRequestController(ISongRequestPageTokenService pageTokens)
    : BaseController
{
    /// <summary>Resolves a public SR-page token to its channel context; 404 on an unknown/disabled token.</summary>
    [HttpGet("{token}")]
    public async Task<IActionResult> GetPage(string token, CancellationToken cancellationToken) =>
        ResultResponse(await pageTokens.ResolveAsync(token, cancellationToken));
}
