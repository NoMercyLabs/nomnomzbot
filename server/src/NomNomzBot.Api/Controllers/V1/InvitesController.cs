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

using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NomNomzBot.Api.Models;
using NomNomzBot.Api.RateLimiting;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The public side of an invite link (S-INVITE-TIER): a signed-out visitor reads what the link gives before
/// signing in with Twitch. Every unusable code (unknown, used up, expired, revoked) answers the same, and a read
/// never spends a redemption.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/billing/invites")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicyNames.Anonymous)]
[Tags("Billing")]
public class InvitesController(IInviteCodeService invites) : BaseController
{
    /// <summary>What an invite code gives: the tier, and how long (N months or forever).</summary>
    [HttpGet("{code}")]
    [ProducesResponseType<StatusResponseDto<PublicInviteDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInvite(string code, CancellationToken ct) =>
        ResultResponse(await invites.GetPublicInviteAsync(code, ct));
}
