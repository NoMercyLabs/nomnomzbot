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
using NomNomzBot.Api.Authorization;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// <c>!permit</c> / <c>!unpermit</c> management surface (roles-permissions §5). Gate 2: issuing/revoking
/// requires <c>permit:issue</c> (Editor+); listing requires <c>roles:read</c> (Moderator+).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/permits")]
[Authorize]
[Tags("Permits")]
public class PermitsController(IPermitService permits, ICurrentUserService currentUser)
    : BaseController
{
    public record GrantRoleBody(
        Guid UserId,
        ManagementRole Role,
        DateTime? ExpiresAt,
        string? Reason
    );

    public record GrantCapabilityBody(
        Guid UserId,
        string ActionKey,
        DateTime? ExpiresAt,
        string? Reason
    );

    /// <summary>List the channel's active permit grants.</summary>
    [HttpGet]
    [RequireAction("roles:read")]
    public async Task<IActionResult> List(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await permits.ListActiveGrantsAsync(broadcasterId, ct));
    }

    /// <summary>Grant a user a management role via permit, with optional expiry and reason.</summary>
    [HttpPost("role")]
    [RequireAction("permit:issue")]
    public async Task<IActionResult> GrantRole(
        string channelId,
        [FromBody] GrantRoleBody body,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(
            await permits.GrantRoleAsync(
                broadcasterId,
                body.UserId,
                body.Role,
                caller,
                body.ExpiresAt,
                body.Reason,
                ct
            )
        );
    }

    /// <summary>Grant a user a single action-key capability via permit, with optional expiry and reason.</summary>
    [HttpPost("capability")]
    [RequireAction("permit:issue")]
    public async Task<IActionResult> GrantCapability(
        string channelId,
        [FromBody] GrantCapabilityBody body,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(
            await permits.GrantCapabilityAsync(
                broadcasterId,
                body.UserId,
                body.ActionKey,
                caller,
                body.ExpiresAt,
                body.Reason,
                ct
            )
        );
    }

    /// <summary>Revoke a user's permit grant, matched by action key or role.</summary>
    [HttpDelete("{userId:guid}")]
    [RequireAction("permit:issue")]
    public async Task<IActionResult> Revoke(
        string channelId,
        Guid userId,
        [FromQuery] string? actionKeyOrRole,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(
            await permits.RevokeAsync(broadcasterId, userId, actionKeyOrRole, caller, ct)
        );
    }

    private bool TryGetCaller(out Guid caller) => Guid.TryParse(currentUser.UserId, out caller);
}
