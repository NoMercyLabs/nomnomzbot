// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
