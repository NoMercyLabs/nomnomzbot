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
using NomNomzBot.Application.Contracts.Gdpr;

namespace NomNomzBot.Application.Services;

/// <summary>
/// The consent / lawful-basis ledger over <c>ConsentRecords</c> (gdpr-crypto.md §3.6) — one active row per
/// <c>(BroadcasterId, SubjectUserId, ConsentType)</c>, latest-wins. Records only what a human affirmatively
/// consented to; inferred facts (e.g. the 18+ account-age inference) never reach this ledger and therefore
/// read back <c>false</c> from <see cref="HasActiveConsentAsync"/>.
/// </summary>
public interface IConsentService
{
    /// <summary>
    /// Upserts the single active consent row to <c>granted</c> (resetting any prior withdrawal), emits
    /// <c>ConsentChangedEvent</c>. Proof-of-consent IP is deliberately not sealed today (the
    /// <c>ConsentRecord</c> entity documents <c>IpAddressCipher</c> as unused by design).
    /// </summary>
    Task<Result<ConsentRecordDto>> GrantAsync(
        GrantConsentRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Flips the active row to <c>withdrawn</c> (stamps <c>WithdrawnAt</c>), emits <c>ConsentChangedEvent</c>.</summary>
    Task<Result> WithdrawAsync(
        Guid subjectUserId,
        Guid? broadcasterId,
        string consentType,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deterministic gate read: true only when a granted, non-withdrawn, non-expired row exists for exactly
    /// <c>(broadcasterId, subjectUserId, consentType)</c>.
    /// </summary>
    Task<Result<bool>> HasActiveConsentAsync(
        Guid subjectUserId,
        Guid? broadcasterId,
        string consentType,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Lists the subject's consent rows. <paramref name="broadcasterId"/> null returns every row (the
    /// my-data page); a value returns that channel's rows plus platform-wide (<c>BroadcasterId=null</c>) rows.
    /// </summary>
    Task<Result<IReadOnlyList<ConsentRecordDto>>> ListForSubjectAsync(
        Guid subjectUserId,
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    );
}
