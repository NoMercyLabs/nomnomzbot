// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Billing.Entities;

namespace NomNomzBot.Infrastructure.Billing;

/// <summary>
/// The one rule for which tier a channel is on. A self-host channel is outside the catalogue (unlimited). A SaaS
/// channel is on its active subscription's tier, or the <c>base</c> tier when it has none, and a live comp grant
/// lifts it when the granted tier ranks higher. Entitlement resolution and the tier-edit blast radius both read
/// this rule, so the count shown before an edit is the set of channels the edit actually reaches.
/// </summary>
internal static class EffectiveTierRule
{
    /// <summary>The tier a SaaS channel without an active subscription is on.</summary>
    public const string BaseTierKey = "base";

    private const string SelfHostPrefix = "self_host";

    public static bool IsSelfHost(string? deploymentMode) =>
        deploymentMode is not null
        && deploymentMode.StartsWith(SelfHostPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>The billed tier, unless a comped tier ranks strictly higher.</summary>
    public static BillingTier? Pick(BillingTier? billed, BillingTier? comped) =>
        comped is not null && (billed is null || comped.SortOrder > billed.SortOrder)
            ? comped
            : billed;
}
