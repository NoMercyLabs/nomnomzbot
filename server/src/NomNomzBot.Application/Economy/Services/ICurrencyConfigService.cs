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
/// Currency definition + earning rules (economy.md §3.1). Pure configuration — no ledger effect.
/// </summary>
public interface ICurrencyConfigService
{
    /// <summary>The channel's currency definition; a channel with no saved row reads the defaults.</summary>
    Task<Result<CurrencyConfigDto>> GetConfigAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Upserts the channel's currency (one per channel). Validates name non-empty, <c>StartingBalance ≥ 0</c>,
    /// and <c>MaxBalance ≥ StartingBalance</c> when set. Returns the saved config.
    /// </summary>
    Task<Result<CurrencyConfigDto>> UpsertConfigAsync(
        Guid broadcasterId,
        UpsertCurrencyConfigRequest request,
        CancellationToken ct = default
    );

    /// <summary>All earning rules for the channel (one per source).</summary>
    Task<Result<IReadOnlyList<EarningRuleDto>>> ListEarningRulesAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Upserts one earning rule by <c>(BroadcasterId, Source)</c>. Validates the source token, <c>Rate ≥ 0</c>,
    /// and non-negative caps. Opt-in (a new rule defaults to disabled). Returns the saved rule.
    /// </summary>
    Task<Result<EarningRuleDto>> UpsertEarningRuleAsync(
        Guid broadcasterId,
        UpsertEarningRuleRequest request,
        CancellationToken ct = default
    );

    /// <summary>Soft-deletes an earning rule. Idempotent; <c>NOT_FOUND</c> if absent.</summary>
    Task<Result> DeleteEarningRuleAsync(
        Guid broadcasterId,
        Guid ruleId,
        CancellationToken ct = default
    );
}
