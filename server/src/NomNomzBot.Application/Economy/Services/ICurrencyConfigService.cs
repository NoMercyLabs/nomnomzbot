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
/// Currency definition + earning rules (economy.md §3.1). Pure configuration — no ledger effect.
/// </summary>
public interface ICurrencyConfigService
{
    /// <summary>The channel's currency definition, or null data when not yet configured (caller seeds defaults).</summary>
    Task<Result<CurrencyConfigDto?>> GetConfigAsync(
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
