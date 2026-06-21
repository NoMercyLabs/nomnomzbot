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
/// Applies earning rules to engagement events (economy.md §3.3). Resolves the rule for the source, gates on
/// role level + idempotency, computes the amount (rate × units) clamped by the per-window cap, then credits via
/// the ledger and emits <c>CurrencyEarnedEvent</c>. Returns the amount actually credited (0 when gated/capped).
/// </summary>
public interface ICurrencyEarningService
{
    /// <summary>Applies the earning rule for one engagement event. Idempotent per <c>(source, EventId)</c>.</summary>
    Task<Result<long>> ApplyEarningAsync(
        Guid broadcasterId,
        EarnRequest request,
        CancellationToken ct = default
    );

    /// <summary>The watch-time presence sweep: applies watch-time earning per present, presence-verified viewer.</summary>
    Task<Result<IReadOnlyList<EarnResultDto>>> ApplyWatchTimeBatchAsync(
        Guid broadcasterId,
        WatchTimeBatchRequest request,
        CancellationToken ct = default
    );
}
