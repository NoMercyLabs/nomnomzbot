// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Platform;

/// <summary>
/// The full outcome of evaluating a staged-rollout <c>FeatureFlag</c> for one channel — for surfaces that must
/// distinguish "no flag governs this key" from "a flag disables it", and explain WHY (rollout-updates §5). The
/// boolean <see cref="IFeatureFlagService.IsEnabledForAsync"/> conflates "no flag" and "flag off" (both false);
/// this keeps them apart via <see cref="Exists"/> so a caller can apply its own default for an ungated key.
/// </summary>
/// <param name="Exists">A <c>FeatureFlag</c> row is defined for the key. When false, no platform gate governs it.</param>
/// <param name="Enabled">Effective state after the full precedence — always false when <paramref name="Exists"/> is false.</param>
/// <param name="Reason">
/// The blocking gate when a defined flag is disabled — one of <see cref="FeatureEntitlementReason"/>; null when
/// enabled or ungated.
/// </param>
/// <param name="RequiredTier">
/// The flag's minimum tier key when the tier floor is the block (<see cref="FeatureEntitlementReason.RequiresTier"/>),
/// for an upgrade prompt; else null.
/// </param>
public sealed record FeatureFlagEvaluation(
    bool Exists,
    bool Enabled,
    string? Reason,
    string? RequiredTier
);

/// <summary>The closed vocabulary of blocking reasons a gated feature reports to a client for its gating UI.</summary>
public static class FeatureEntitlementReason
{
    /// <summary>The channel's active billing tier ranks below the flag's minimum tier; upgradable.</summary>
    public const string RequiresTier = "REQUIRES_TIER";

    /// <summary>The flag's deployment-mode gate excludes this deployment (saas vs self-host); not upgradable.</summary>
    public const string Deployment = "DEPLOYMENT";

    /// <summary>Blocked for a non-actionable reason — global off, not yet in the rollout ramp, or an admin override.</summary>
    public const string Unavailable = "UNAVAILABLE";
}
