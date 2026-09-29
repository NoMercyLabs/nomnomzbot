// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Mints the act-as token for a support-access grant. Shared by the start of an impersonation
/// (<see cref="PlatformAdminService.StartImpersonationAsync"/>) and its refresh
/// (<see cref="ImpersonationSessionService.RefreshAsync"/>), so both produce the same token for the same
/// target: the target's identity, tenant and roles exactly as their own login mints them, never the operator's.
/// </summary>
internal static class ImpersonationTokenMinter
{
    public static async Task<ImpersonationTokenDto> MintAsync(
        IApplicationDbContext db,
        IJwtTokenService jwt,
        IamRoleAssignment grant,
        User target,
        CancellationToken ct
    )
    {
        // The target's own broadcaster channel scopes the `tenant` claim, exactly like the target's own login
        // — null when the target owns no channel (a moderator or viewer). Ordered so a user who owns several
        // channels always resolves the same one.
        Guid? tenantId = await db
            .Channels.Where(c => c.OwnerUserId == target.Id)
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

        // The acting operator is named ONLY on the non-authoritative `act` claim, never as a role.
        IamPrincipal? actor = await db.IamPrincipals.FirstOrDefaultAsync(
            p => p.Id == grant.PrincipalId,
            ct
        );

        // CRITICAL INVARIANT: roles + identity are the TARGET's. `sid` is the GRANT id: closing the grant ends
        // this token, and the token's lifetime is clamped to never outlive the grant.
        string accessToken = jwt.GenerateAccessToken(
            target.Id,
            target.Username,
            tenantId,
            grant.Id,
            RolesFor(target),
            idp: target.Platform,
            actorUserId: ActorUserId(grant, actor),
            maxExpiresAt: grant.ExpiresAt
        );

        DateTime expiresAt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken).ValidTo;
        return new(accessToken, expiresAt, grant.Id, ToDto(target));
    }

    /// <summary>
    /// The value the <c>act</c> claim carries for <paramref name="grant"/>'s operator: their user id, or the
    /// principal id for a principal with no backing user.
    /// </summary>
    public static string ActorUserId(IamRoleAssignment grant, IamPrincipal? actor) =>
        (actor?.UserId ?? grant.PrincipalId).ToString();

    /// <summary>
    /// The role set an access token carries for <paramref name="user"/> — identical to
    /// <c>SessionService.RolesFor</c>, the normal-login source of truth.
    /// </summary>
    private static IEnumerable<string> RolesFor(User user) =>
        user.IsPlatformPrincipal ? ["user", "admin"] : ["user"];

    /// <summary>The impersonated user's profile, mirroring <c>UserService.ToDto</c> (LastLoginAt = UpdatedAt).</summary>
    private static UserDto ToDto(User u) =>
        new(
            u.Id.ToString(),
            u.Username,
            u.DisplayName,
            u.ProfileImageUrl,
            null,
            u.CreatedAt,
            u.UpdatedAt
        );
}
