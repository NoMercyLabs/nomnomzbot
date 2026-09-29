// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Claims;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Infrastructure.Platform.Auth;

namespace NomNomzBot.Api.Authentication;

/// <summary>
/// The JwtBearer <c>OnTokenValidated</c> check for act-as tokens: an act-as token authenticates only while
/// its support-access grant is open. The <c>sid</c> revocation alone covered one way out (the operator's
/// Exit); ending the grant any other way — ending the support session, an IAM revoke, the expiry job — left
/// the act-as token working until it expired. Tokens without an actor are not touched here.
/// </summary>
public static class ImpersonationSessionCheck
{
    /// <summary>True when the token names an operator acting as its subject (the <c>act</c> claim).</summary>
    public static bool IsActAsToken(ClaimsPrincipal? principal) =>
        !string.IsNullOrEmpty(principal?.FindFirstValue(JwtTokenService.ActorClaim));

    /// <summary>
    /// The support grant (<c>sid</c>) and the operator (<c>act</c>) an act-as token names. False for a token
    /// that is not act-as, or whose session cannot be read.
    /// </summary>
    public static bool TryReadActAs(
        ClaimsPrincipal? principal,
        out Guid accessGrantId,
        out string actorUserId
    )
    {
        accessGrantId = Guid.Empty;
        actorUserId = string.Empty;
        if (principal?.FindFirstValue(JwtTokenService.ActorClaim) is not { Length: > 0 } actor)
            return false;

        actorUserId = actor;
        return Guid.TryParse(
            principal.FindFirstValue(JwtTokenService.SessionClaim),
            out accessGrantId
        );
    }

    public static async Task<bool> IsEndedAsync(
        ClaimsPrincipal? principal,
        IImpersonationSessionService sessions,
        CancellationToken cancellationToken = default
    )
    {
        if (!IsActAsToken(principal))
            return false;

        // An act-as token without a readable session cannot be tied to a grant — refuse it.
        return !TryReadActAs(principal, out Guid accessGrantId, out _)
            || !await sessions.IsActiveAsync(accessGrantId, cancellationToken);
    }
}
