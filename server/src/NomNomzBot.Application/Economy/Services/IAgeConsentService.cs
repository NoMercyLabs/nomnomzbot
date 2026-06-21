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
using NomNomzBot.Application.DTOs.Economy;

namespace NomNomzBot.Application.Economy.Services;

/// <summary>
/// The lightweight 18+ gambling gate (economy.md §3.6) — an account-age auto-pass plus a one-tap self-confirm,
/// not a KYC ritual. Runs only when a streamer set <c>Requires18Plus</c> on a gambling game.
/// </summary>
public interface IAgeConsentService
{
    /// <summary>
    /// True iff the viewer passes the gate by ANY of three methods (precedence): (1) an affirmative consent;
    /// (2) account age ≥ the threshold (MONOTONIC); (3) live Twitch personnel type. Fail-closed on any
    /// uncertainty. Materializes a distinct K.8 inference row for (2)/(3) — never a ConsentRecords consent row.
    /// </summary>
    Task<Result<bool>> HasGrantedAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        CancellationToken ct = default
    );

    /// <summary>Records explicit consent: the authoritative ConsentRecords row + the K.8 cache, in one step; publishes the granted event. Idempotent.</summary>
    Task<Result<AgeConsentDto>> GrantAsync(
        Guid broadcasterId,
        GrantAgeConsentRequest request,
        CancellationToken ct = default
    );

    /// <summary>Withdraws consent: ConsentRecords status → withdrawn + cache revoked; publishes the revoked event. Idempotent.</summary>
    Task<Result<AgeConsentDto>> RevokeAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        CancellationToken ct = default
    );
}
