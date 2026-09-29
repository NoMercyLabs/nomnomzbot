// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// The life of an act-as (impersonation) session after it started. The session IS its support-access grant:
/// the act-as token's <c>sid</c> is the grant id, so the token is only good while that grant is open.
/// Starting and ending stay on <see cref="IPlatformAdminService"/> (they need the operator's authority);
/// this service answers the questions the act-as token itself can ask.
/// </summary>
public interface IImpersonationSessionService
{
    /// <summary>
    /// Reads an act-as token whose signature is ours, ignoring its lifetime. Null when the token is not
    /// signed by this deployment or carries no actor — i.e. it is not an act-as token.
    /// </summary>
    ActAsTokenClaims? ReadActAsToken(string accessToken);

    /// <summary>
    /// True while the support-access grant behind <paramref name="accessGrantId"/> is open: not revoked, not
    /// expired, and its session not revoked. Every revoke path (exit, grant end, IAM revoke, expiry job) closes
    /// the grant, so this one check ends the act-as token on all of them.
    /// </summary>
    Task<bool> IsActiveAsync(Guid accessGrantId, CancellationToken ct = default);

    /// <summary>
    /// Re-mints the act-as token for the SAME target under the SAME grant, so a token refresh never hands
    /// back the operator. The presented token may be expired; its signature must be ours and its grant must
    /// still be open. <c>IMPERSONATION_ENDED</c> when the grant is closed or the target left its channel.
    /// </summary>
    Task<Result<ImpersonationTokenDto>> RefreshAsync(
        string actAsAccessToken,
        CancellationToken ct = default
    );

    /// <summary>
    /// The operator's IAM principal behind an OPEN act-as session, for ending it from inside — the Exit pressed
    /// while acting, when the only token in hand is the act-as token. <paramref name="actorUserId"/> is that
    /// token's <c>act</c> claim and must name the operator the grant belongs to (<c>FORBIDDEN</c> otherwise).
    /// <c>IMPERSONATION_ENDED</c> when the session is already over.
    /// </summary>
    Task<Result<Guid>> FindOperatorAsync(
        Guid accessGrantId,
        string actorUserId,
        CancellationToken ct = default
    );
}
