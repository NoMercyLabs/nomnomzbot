// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.AspNetCore.Authorization;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;

namespace NomNomzBot.Api.Authorization;

/// <summary>
/// Gate 2 enforcement (roles-permissions §6): resolves the caller (JWT user id) and the current tenant
/// (resolved by <c>TenantResolutionMiddleware</c> before authorization runs) and asks
/// <see cref="IActionAuthorizationService"/> whether the caller clears the action's effective required level.
/// Succeeds only on an explicit allow; otherwise leaves the requirement unmet → 403.
/// </summary>
public sealed class ActionAuthorizationHandler(
    IActionAuthorizationService authorization,
    ICurrentUserService currentUser,
    ICurrentTenantService currentTenant
) : AuthorizationHandler<ActionAuthorizationRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActionAuthorizationRequirement requirement
    )
    {
        if (!currentUser.IsAuthenticated || !Guid.TryParse(currentUser.UserId, out Guid userId))
            return;

        if (currentTenant.BroadcasterId is not Guid broadcasterId || broadcasterId == Guid.Empty)
            return;

        Result<bool> result = await authorization.AuthorizeActionAsync(
            userId,
            broadcasterId,
            requirement.ActionKey
        );
        if (result.IsSuccess && result.Value)
            context.Succeed(requirement);
    }
}
