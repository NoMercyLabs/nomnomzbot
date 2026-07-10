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
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
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
public class RolesController(
    IMembershipService memberships,
    IRoleResolver roleResolver,
    ICurrentUserService currentUser
) : BaseController
{
    public record SetRoleBody(Guid UserId, ManagementRole Role);

    /// <summary>List the channel's management-role memberships, paginated (flat PaginatedResponse shape).</summary>
    [HttpGet]
    [RequireAction("roles:read")]
    [ProducesResponseType<PaginatedResponse<ChannelMembershipDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        string channelId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        // The flat PaginatedResponse ({ data: [...] }) shape every other list endpoint emits — the dashboard's
        // PaginatedEnvelope reads `data` as the array, so wrapping a PagedList in StatusResponseDto (data: {items})
        // would break deserialization. Mirror EventResponses/Commands/etc.: GetPaginatedResponse over the PagedList.
        Result<PagedList<ChannelMembershipDto>> result = await memberships.ListMembershipsAsync(
            broadcasterId,
            page,
            pageSize,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(
            result.Value,
            new PageRequestDto { Page = page, Take = pageSize }
        );
    }

    /// <summary>Assign or change a user's management role on the channel, recorded as a bot grant.</summary>
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

    /// <summary>Remove a user's management role from the channel.</summary>
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

    /// <summary>
    /// The authenticated caller's own resolved access on this channel (roles-permissions §5.1). The shell calls
    /// this on session establish to learn the caller's effective <see cref="ManagementRole"/> and drive the
    /// role-correct sidebar + write affordances. Self-introspection, so it is gated by entry (Gate 1) only — a
    /// pure viewer with no management role must be able to learn they have none (and so be routed to the
    /// participation-only surface), which a <c>roles:read</c> (Moderator) floor would forbid.
    /// </summary>
    [HttpGet("effective/me")]
    [ProducesResponseType<StatusResponseDto<ResolvedAccessDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> EffectiveMe(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(await roleResolver.ResolveAccessAsync(caller, broadcasterId, ct));
    }

    /// <summary>
    /// A channel member's resolved access (roles-permissions §5.1). Floors at <c>roles:read</c> (Moderator) —
    /// inspecting another user's access is a management read; <see cref="EffectiveMe"/> is the unfloored
    /// self-introspection sibling.
    /// </summary>
    [HttpGet("effective/{userId:guid}")]
    [RequireAction("roles:read")]
    [ProducesResponseType<StatusResponseDto<ResolvedAccessDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Effective(string channelId, Guid userId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await roleResolver.ResolveAccessAsync(userId, broadcasterId, ct));
    }

    private bool TryGetCaller(out Guid caller) => Guid.TryParse(currentUser.UserId, out caller);
}
