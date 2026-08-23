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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Infrastructure.Platform.Auth;

namespace NomNomzBot.Api.Authentication;

/// <summary>
/// The JwtBearer <c>OnTokenValidated</c> revocation check (S098b), pulled out of Program.cs so it is
/// unit-testable without standing up the ASP.NET auth pipeline. A token whose <c>sid</c> claim is missing
/// or unparsable is treated as NOT revoked here — malformed/legacy tokens fail on their own missing claims
/// elsewhere in the pipeline, this check only ever narrows acceptance further, never widens it.
/// </summary>
public static class SessionRevocationCheck
{
    public static async Task<bool> IsSessionRevokedAsync(
        ClaimsPrincipal? principal,
        ISessionRevocationService revocation,
        CancellationToken cancellationToken = default
    )
    {
        Claim? sidClaim = principal?.FindFirst(JwtTokenService.SessionClaim);
        return sidClaim is not null
            && Guid.TryParse(sidClaim.Value, out Guid sessionId)
            && await revocation.IsRevokedAsync(sessionId, cancellationToken);
    }
}
