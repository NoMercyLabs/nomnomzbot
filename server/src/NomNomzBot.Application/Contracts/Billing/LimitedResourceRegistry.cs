// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Billing;

namespace NomNomzBot.Application.Contracts.Billing;

/// <summary>
/// One declared limit lever (S-BUDGETS-a). Extends scaling-qos.md §8.
/// <paramref name="SafetyBaseline"/>: NEAR_FREE only — the abuse floor applied uniformly to every
/// tenant, self-host included — never tier-scaled. COST_DRIVING resources ignore this; their limit
/// comes from the tier's <c>TierLimit</c> row via <see cref="IBillingTierService"/> (self-host resolves
/// to unlimited there).
/// </summary>
public sealed record LimitedResourceDescriptor(
    string LimitKey,
    ResourceClass Class,
    string DisplayName,
    long SafetyBaseline
);

/// <summary>
/// The single registry of every limited resource in the system (S-BUDGETS-a). Declaring a resource here is what
/// makes the structural guard test pass — an entity carrying
/// <c>[CountedResource]</c> with no matching entry here fails loud, on purpose: a <c>TierLimit</c>/registry
/// surface that nothing enforces is a truthful-data violation, not a feature.
/// </summary>
public static class LimitedResourceRegistry
{
    public static readonly IReadOnlyList<LimitedResourceDescriptor> Resources =
    [
        // NEAR_FREE — one DB row, effectively free to serve. Capped only against abuse, at a generous floor,
        // for every tenant including self-host. Never tier-scaled, never sold as headroom.
        new("custom_commands", ResourceClass.NearFree, "Custom commands", SafetyBaseline: 1500),
        new("timers", ResourceClass.NearFree, "Timers", SafetyBaseline: 200),
        new("event_responses", ResourceClass.NearFree, "Event responses", SafetyBaseline: 400),
        new(
            "response_variations_per_trigger",
            ResourceClass.NearFree,
            "Response variations per trigger",
            SafetyBaseline: 100
        ),
        // COST_DRIVING — maps to a real bill. Tier-scaled via TierLimit; self-host resolves to unlimited there
        // (the operator pays their own hosting). SafetyBaseline is unused for this class.
        new(
            "tts_max_characters",
            ResourceClass.CostDriving,
            "TTS characters per month",
            SafetyBaseline: 0
        ),
        new(
            "sandbox_exec_ms",
            ResourceClass.CostDriving,
            "Script CPU time per month",
            SafetyBaseline: 0
        ),
        // Stored bytes are a real bill (disk + egress + backup) — a live gauge (SUM of currently-live rows'
        // SizeBytes), never an event-accumulated counter: deleting a clip/asset must lower usage immediately,
        // unlike a monthly TTS/sandbox allowance. Metered via IResourceQuotaService.GetCurrentCountAsync, the
        // same seam the write-path budget check reads.
        new(
            "sound_clip_storage_bytes",
            ResourceClass.CostDriving,
            "Sound clip storage",
            SafetyBaseline: 0
        ),
        new(
            "channel_asset_storage_bytes",
            ResourceClass.CostDriving,
            "Channel asset storage",
            SafetyBaseline: 0
        ),
    ];

    private static readonly Dictionary<string, LimitedResourceDescriptor> ByKey =
        Resources.ToDictionary(r => r.LimitKey, StringComparer.Ordinal);

    public static bool TryGet(string limitKey, out LimitedResourceDescriptor descriptor) =>
        ByKey.TryGetValue(limitKey, out descriptor!);
}
