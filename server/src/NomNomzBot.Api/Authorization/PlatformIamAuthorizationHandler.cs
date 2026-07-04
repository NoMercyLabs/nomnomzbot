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
/// Plane-C enforcement (roles-permissions §3.7, platform-conventions §5): resolves the caller's IAM principal
/// and asks <see cref="IPlatformIamService.AuthorizePlatformAsync"/> whether it holds the policy's permission
/// key — which audits every decision on SaaS.
/// <para>
/// SECURITY ORDER MATTERS: the caller must FIRST carry the platform-principal marker (the <c>admin</c> role
/// claim, minted only for <c>User.IsPlatformPrincipal</c> — the exact claim the legacy
/// <c>[Authorize(Roles="admin")]</c> gates checked). Only then is the IAM service consulted.
/// <see cref="IPlatformIamService.AuthorizePlatformAsync"/> short-circuits to ALLOW when zero
/// <c>IamPrincipal</c>s exist (self-host = operator implicitly full), so calling it for arbitrary
/// authenticated users would hand every viewer Plane-C access on self-host.
/// </para>
/// </summary>
public sealed class PlatformIamAuthorizationHandler(
    IPlatformIamService iam,
    ICurrentUserService currentUser
) : AuthorizationHandler<PlatformIamRequirement>
{
    /// <summary>The role claim value minted for <c>User.IsPlatformPrincipal</c> (SessionService.RolesFor).</summary>
    public const string PlatformPrincipalRole = "admin";

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PlatformIamRequirement requirement
    )
    {
        if (!currentUser.IsAuthenticated || !Guid.TryParse(currentUser.UserId, out Guid userId))
            return;

        // The platform-principal marker gates ENTRY to this plane; without it the IAM service is never
        // consulted (its self-host short-circuit would otherwise allow any authenticated caller).
        if (context.User?.IsInRole(PlatformPrincipalRole) != true)
            return;

        Result<IamPrincipalDto?> principal = await iam.ResolvePrincipalAsync(userId);
        if (principal.IsFailure)
            return;

        if (principal.Value is null)
        {
            // No principal row for a platform-marked caller. Self-host (zero principals anywhere): the
            // operator is implicitly full — allow. SaaS (principals exist): a marker without a principal
            // row is a misconfiguration — fail closed; never fabricate a principal id for the audit log.
            if (!await iam.HasAnyPrincipalsAsync())
                context.Succeed(requirement);
            return;
        }

        Result<bool> allowed = await iam.AuthorizePlatformAsync(
            principal.Value.Id,
            requirement.PermissionKey,
            targetBroadcasterId: null,
            breakGlass: false,
            justification: null
        );
        if (allowed.IsSuccess && allowed.Value)
            context.Succeed(requirement);
    }
}
