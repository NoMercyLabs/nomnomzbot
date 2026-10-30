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

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NomNomzBot.Domain.Billing.Entities;

namespace NomNomzBot.Infrastructure.Billing;

/// <summary>
/// The tier of a channel with no subscription and no live gift: the configured
/// <see cref="BillingOptions.DefaultTierKey"/>, or <c>base</c> when that key names no live tier. Entitlement
/// resolution and the tier-edit blast radius both read it, so both agree on where those channels are.
/// </summary>
public sealed class UnsubscribedTierPolicy(
    IOptions<BillingOptions> options,
    ILogger<UnsubscribedTierPolicy> logger
)
{
    private int _warned;

    /// <summary>The tier keys <see cref="Pick"/> may choose from: the configured key, then <c>base</c>.</summary>
    public IReadOnlyList<string> CandidateKeys => [ConfiguredKey, EffectiveTierRule.BaseTierKey];

    private string ConfiguredKey => options.Value.DefaultTierKey;

    /// <summary>The configured tier among <paramref name="liveTiers"/>, else <c>base</c>.</summary>
    public BillingTier? Pick(IEnumerable<BillingTier> liveTiers)
    {
        List<BillingTier> tiers = [.. liveTiers];
        BillingTier? configured = tiers.FirstOrDefault(t => t.Key == ConfiguredKey);
        if (configured is not null)
            return configured;

        WarnOnceAboutMissingTier();
        return tiers.FirstOrDefault(t => t.Key == EffectiveTierRule.BaseTierKey);
    }

    private void WarnOnceAboutMissingTier()
    {
        if (ConfiguredKey == EffectiveTierRule.BaseTierKey)
            return;
        if (Interlocked.Exchange(ref _warned, 1) == 1)
            return;

        logger.LogWarning(
            "Billing:DefaultTierKey {TierKey} names no live tier; channels with no plan fall back to {BaseTierKey}.",
            ConfiguredKey,
            EffectiveTierRule.BaseTierKey
        );
    }
}
