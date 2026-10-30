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

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Billing.Entities;

namespace NomNomzBot.Infrastructure.Content.Billing;

/// <summary>
/// Seeds the GLOBAL billing tiers + their quota limits (monetization-billing.md §8.6) — pure reference data, no
/// FK deps. The hosted plans are <c>hosted_free</c> (Free) / <c>base</c> ($3.99) / <c>pro</c> ($7.99) /
/// <c>premium</c> ($14.99); <c>free</c>
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
        Dictionary<string, BillingTier> present = await _db.BillingTiers.ToDictionaryAsync(
            t => t.Key,
            StringComparer.Ordinal,
            ct
        );

        foreach (TierSeed seed in Catalogue)
        {
            if (present.TryGetValue(seed.Key, out BillingTier? existing))
            {
                // The tier exists — backfill any limit KEYS added to the catalogue since it was seeded
                // (values of already-present keys are never overwritten: operators may have tuned them).
                // A key an operator REMOVED is a soft-deleted row: it counts as present here, so the removal
                // holds across restarts instead of being re-inserted beside the deleted row on every boot.
                HashSet<string> existingKeys = (
                    await _db
                        .TierLimits.IgnoreQueryFilters()
                        .Where(l => l.TierId == existing.Id)
                        .Select(l => l.LimitKey)
                        .ToListAsync(ct)
                ).ToHashSet(StringComparer.Ordinal);

                foreach ((string limitKey, long limitValue) in seed.Limits)
                    if (!existingKeys.Contains(limitKey))
                        _db.TierLimits.Add(
                            new()
                            {
                                TierId = existing.Id,
                                LimitKey = limitKey,
                                LimitValue = limitValue,
                            }
                        );
                continue;
            }

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
                    new()
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
        // The hosted Free plan: no code scripts, short TTS lines with a monthly total, small storage.
        new(
            "hosted_free",
            "Free",
            0,
            false,
            false,
            true,
            5,
            [
                ("tts_max_characters", 200),
                ("tts_monthly_characters", 25_000),
                ("sandbox_exec_ms", 0),
                ("sound_clip_storage_bytes", 25L * 1024 * 1024), // 25 MB
                ("channel_asset_storage_bytes", 25L * 1024 * 1024), // 25 MB
            ]
        ),
        new(
            "base",
            "Base",
            399,
            false,
            false,
            true,
            10,
            [
                ("tts_max_characters", 500),
                ("tts_monthly_characters", -1), // no monthly TTS total
                // Monthly sandbox budget (custom-code.md §3.3) — ~10 min of script execution per month.
                ("sandbox_exec_ms", 600_000),
                // Live storage gauges (S-BUDGETS-b5) — disk + egress + backup headroom, in bytes.
                ("sound_clip_storage_bytes", 100L * 1024 * 1024), // 100 MB
                ("channel_asset_storage_bytes", 100L * 1024 * 1024), // 100 MB
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
                ("tts_max_characters", 2000),
                ("tts_monthly_characters", -1),
                ("sandbox_exec_ms", 2_700_000), // ~45 min / month
                ("sound_clip_storage_bytes", 500L * 1024 * 1024), // 500 MB
                ("channel_asset_storage_bytes", 500L * 1024 * 1024), // 500 MB
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
                ("tts_max_characters", 8000),
                ("tts_monthly_characters", -1),
                ("sandbox_exec_ms", 7_200_000), // ~2 h / month
                ("sound_clip_storage_bytes", 2L * 1024 * 1024 * 1024), // 2 GB
                ("channel_asset_storage_bytes", 2L * 1024 * 1024 * 1024), // 2 GB
            ]
        ),
    ];
}
