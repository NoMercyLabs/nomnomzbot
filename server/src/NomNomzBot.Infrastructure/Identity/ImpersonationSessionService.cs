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
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Platform.Auth;

namespace NomNomzBot.Infrastructure.Identity;

/// <inheritdoc />
public sealed class ImpersonationSessionService(
    IApplicationDbContext db,
    IJwtTokenService jwt,
    ISessionRevocationService sessionRevocation,
    ITenantMemberDirectoryService memberDirectory,
    TimeProvider clock
) : IImpersonationSessionService
{
    private const string EndedCode = "IMPERSONATION_ENDED";

    public ActAsTokenClaims? ReadActAsToken(string accessToken)
    {
        // Same keys, issuer, audience and pinned algorithm as bearer validation — only the lifetime check is
        // off, so an act-as token that just expired can still be recognised (and refused as the operator).
        TokenValidationParameters parameters = jwt.GetValidationParameters().Clone();
        parameters.ValidateLifetime = false;

        ClaimsPrincipal principal;
        try
        {
            principal = new JwtSecurityTokenHandler().ValidateToken(accessToken, parameters, out _);
        }
        catch (Exception)
        {
            return null;
        }

        string? actor = principal.FindFirstValue(JwtTokenService.ActorClaim);
        if (
            string.IsNullOrEmpty(actor)
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out Guid subject)
            || !Guid.TryParse(
                principal.FindFirstValue(JwtTokenService.SessionClaim),
                out Guid grant
            )
        )
            return null;

        return new(subject, grant, actor);
    }

    public async Task<bool> IsActiveAsync(Guid accessGrantId, CancellationToken ct = default) =>
        await FindOpenGrantAsync(accessGrantId, ct) is not null
        && !await sessionRevocation.IsRevokedAsync(accessGrantId, ct);

    public async Task<Result<ImpersonationTokenDto>> RefreshAsync(
        string actAsAccessToken,
        CancellationToken ct = default
    )
    {
        ActAsTokenClaims? claims = ReadActAsToken(actAsAccessToken);
        if (claims is null)
            return Result.Failure<ImpersonationTokenDto>(
                "Not an act-as token of this deployment.",
                "UNAUTHORIZED"
            );

        IamRoleAssignment? grant = await FindOpenGrantAsync(claims.AccessGrantId, ct);
        if (grant is null || await sessionRevocation.IsRevokedAsync(grant.Id, ct))
            return Ended<ImpersonationTokenDto>();

        // The token must name the operator the grant belongs to — a token re-signed for another operator's
        // grant is not this session.
        IamPrincipal? actor = await db.IamPrincipals.FirstOrDefaultAsync(
            p => p.Id == grant.PrincipalId,
            ct
        );
        if (ImpersonationTokenMinter.ActorUserId(grant, actor) != claims.ActorUserId)
            return Ended<ImpersonationTokenDto>();

        User? target = await db.Users.FirstOrDefaultAsync(u => u.Id == claims.SubjectUserId, ct);
        if (
            target is null
            || grant.ScopeChannelId is not Guid scopeChannelId
            || !await memberDirectory.IsMemberAsync(scopeChannelId, target.Id, ct)
        )
            return Ended<ImpersonationTokenDto>();

        return Result.Success(await ImpersonationTokenMinter.MintAsync(db, jwt, grant, target, ct));
    }

    public async Task<Result<Guid>> FindOperatorAsync(
        Guid accessGrantId,
        string actorUserId,
        CancellationToken ct = default
    )
    {
        IamRoleAssignment? grant = await FindOpenGrantAsync(accessGrantId, ct);
        if (grant is null)
            return Ended<Guid>();

        IamPrincipal? actor = await db.IamPrincipals.FirstOrDefaultAsync(
            p => p.Id == grant.PrincipalId,
            ct
        );
        return ImpersonationTokenMinter.ActorUserId(grant, actor) == actorUserId
            ? Result.Success(grant.PrincipalId)
            : Result.Failure<Guid>("This act-as session belongs to another operator.", "FORBIDDEN");
    }

    private Task<IamRoleAssignment?> FindOpenGrantAsync(Guid accessGrantId, CancellationToken ct)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        return db.IamRoleAssignments.FirstOrDefaultAsync(
            a =>
                a.Id == accessGrantId
                && a.RevokedAt == null
                && a.ExpiresAt != null
                && a.ExpiresAt > now,
            ct
        );
    }

    private static Result<T> Ended<T>() =>
        Result.Failure<T>("The impersonation session has ended.", EndedCode);
}
