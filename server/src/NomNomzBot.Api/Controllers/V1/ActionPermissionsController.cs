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

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Per-action permission matrix + overrides (roles-permissions §5). Gate 2: reading the matrix requires
/// <c>roles:read</c> (Moderator+); changing an action's required level requires <c>roles:manage</c>
/// (Broadcaster, Critical). Overrides are clamped to the action floor by the service.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/action-permissions")]
[Authorize]
[Tags("ActionPermissions")]
public class ActionPermissionsController(
    IActionAuthorizationService authorization,
    ICurrentUserService currentUser
) : BaseController
{
    public record SetOverrideBody(int Level);

    /// <summary>Read the channel's full action-permission matrix (defaults plus overrides).</summary>
    [HttpGet]
    [RequireAction("roles:read")]
    public async Task<IActionResult> Matrix(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await authorization.GetActionMatrixAsync(broadcasterId, ct));
    }

    /// <summary>Override the required level for an action key (clamped to the action's floor).</summary>
    [HttpPut("{actionKey}")]
    [RequireAction("roles:manage")]
    public async Task<IActionResult> SetOverride(
        string channelId,
        string actionKey,
        [FromBody] SetOverrideBody body,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(
            await authorization.SetActionOverrideAsync(
                broadcasterId,
                actionKey,
                body.Level,
                caller,
                ct
            )
        );
    }

    /// <summary>Reset an action key's override back to its default required level.</summary>
    [HttpDelete("{actionKey}")]
    [RequireAction("roles:manage")]
    public async Task<IActionResult> ResetOverride(
        string channelId,
        string actionKey,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(
            await authorization.ResetActionOverrideAsync(broadcasterId, actionKey, caller, ct)
        );
    }

    private bool TryGetCaller(out Guid caller) => Guid.TryParse(currentUser.UserId, out caller);
}
