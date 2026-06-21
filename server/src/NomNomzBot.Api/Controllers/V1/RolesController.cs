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
/// Channel-management membership surface (roles-permissions §5). Gate 2: listing requires <c>roles:read</c>
/// (Moderator+); assigning/removing a management role requires <c>roles:manage</c> (Broadcaster, Critical).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/roles")]
[Authorize]
[Tags("Roles")]
public class RolesController(IMembershipService memberships, ICurrentUserService currentUser)
    : BaseController
{
    public record SetRoleBody(Guid UserId, ManagementRole Role);

    [HttpGet]
    [RequireAction("roles:read")]
    public async Task<IActionResult> List(
        string channelId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(
            await memberships.ListMembershipsAsync(broadcasterId, page, pageSize, ct)
        );
    }

    [HttpPut]
    [RequireAction("roles:manage")]
    public async Task<IActionResult> SetRole(
        string channelId,
        [FromBody] SetRoleBody body,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(
            await memberships.SetManagementRoleAsync(
                broadcasterId,
                body.UserId,
                body.Role,
                MembershipSource.BotGrant,
                caller,
                ct
            )
        );
    }

    [HttpDelete("{userId:guid}")]
    [RequireAction("roles:manage")]
    public async Task<IActionResult> RemoveRole(string channelId, Guid userId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(
            await memberships.RemoveManagementRoleAsync(broadcasterId, userId, caller, ct)
        );
    }

    private bool TryGetCaller(out Guid caller) => Guid.TryParse(currentUser.UserId, out caller);
}
