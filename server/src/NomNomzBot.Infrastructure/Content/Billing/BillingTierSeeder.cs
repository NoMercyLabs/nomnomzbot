// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Billing.Entities;

namespace NomNomzBot.Infrastructure.Content.Billing;

/// <summary>
/// Seeds the GLOBAL billing tiers + their quota limits (monetization-billing.md §8.6) — pure reference data, no
/// FK deps. The hosted plans are <c>base</c> ($3.99) / <c>pro</c> ($7.99) / <c>premium</c> ($14.99); <c>free</c>
/// is the non-public self-host marker (no seeded limits — the entitlement resolves them to unlimited).
/// Idempotent: upserts by the natural key <see cref="BillingTier.Key"/>.
/// </summary>
public sealed class BillingTierSeeder : ISeeder
{
    private readonly IApplicationDbContext _db;

    public BillingTierSeeder(IApplicationDbContext db) => _db = db;

    public int Order => 6;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        HashSet<string> present = (
            await _db.BillingTiers.Select(t => t.Key).ToListAsync(ct)
        ).ToHashSet(StringComparer.Ordinal);

        foreach (TierSeed seed in Catalogue)
        {
            if (present.Contains(seed.Key))
                continue;

            BillingTier tier = new()
            {
                Key = seed.Key,
                DisplayName = seed.DisplayName,
                PriceCents = seed.PriceCents,
                Currency = "usd",
                AllowsCustomBotName = seed.AllowsCustomBotName,
                PrioritySupport = seed.PrioritySupport,
                IsPublic = seed.IsPublic,
                SortOrder = seed.SortOrder,
            };
            _db.BillingTiers.Add(tier);

            foreach ((string limitKey, long limitValue) in seed.Limits)
                _db.TierLimits.Add(
                    new TierLimit
                    {
                        TierId = tier.Id,
                        LimitKey = limitKey,
                        LimitValue = limitValue,
                    }
                );
        }
    }

    private readonly record struct TierSeed(
        string Key,
        string DisplayName,
        int PriceCents,
        bool AllowsCustomBotName,
        bool PrioritySupport,
        bool IsPublic,
        int SortOrder,
        (string Key, long Value)[] Limits
    );

    private static readonly TierSeed[] Catalogue =
    [
        // The non-public self-host marker — no limits (the entitlement resolves every key to unlimited).
        new("free", "Self-host", 0, true, false, false, 0, []),
        new(
            "base",
            "Base",
            399,
            false,
            false,
            true,
            10,
            [
                ("response_variations_per_trigger", 15),
                ("custom_commands", 100),
                ("timers", 20),
                ("event_responses", 40),
                ("tts_max_characters", 500),
            ]
        ),
        new(
            "pro",
            "Pro",
            799,
            true,
            true,
            true,
            20,
            [
                ("response_variations_per_trigger", 40),
                ("custom_commands", 400),
                ("timers", 60),
                ("event_responses", 120),
                ("tts_max_characters", 2000),
            ]
        ),
        new(
            "premium",
            "Premium",
            1499,
            true,
            true,
            true,
            30,
            [
                ("response_variations_per_trigger", 100),
                ("custom_commands", 1500),
                ("timers", 200),
                ("event_responses", 400),
                ("tts_max_characters", 8000),
            ]
        ),
    ];
}
