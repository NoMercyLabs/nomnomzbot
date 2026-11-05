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

namespace NomNomzBot.Infrastructure.Platform;

/// <summary>
/// Concurrency sizing for the chat/command hot path (S-CONCURRENCY-DRAIN). An explicit value here always
/// wins over every derived default below. Self-host derives its own default from the host's CPU cores and
/// database engine (<see cref="ScalingDefaults"/>) instead of a fixed number, since a self-hosted box's
/// capacity varies wildly (a Raspberry Pi vs. a dedicated server). SaaS instead scales by subscription tier,
/// since the shared Postgres + Redis tier has far more headroom than one self-host SQLite box ever will.
/// </summary>
public sealed class ScalingOptions
{
    public const string SectionName = "Scaling";

    /// <summary>Overrides the derived self-host per-channel pipeline cap. Null = derive it.</summary>
    public int? SelfHostMaxConcurrentPerChannel { get; set; }

    /// <summary>Overrides the derived self-host instance-wide notification-dispatch concurrency. Null = derive it.</summary>
    public int? SelfHostMaxConcurrentNotificationDispatch { get; set; }

    /// <summary>Per-tier SaaS per-channel pipeline cap, keyed by <c>EntitlementDto.TierKey</c>.</summary>
    public Dictionary<string, int> SaasTierMaxConcurrentPerChannel { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["free"] = 3,
            ["base"] = 5,
            ["pro"] = 10,
            ["premium"] = 20,
        };

    /// <summary>Fallback SaaS per-channel cap for a tier key absent from <see cref="SaasTierMaxConcurrentPerChannel"/>.</summary>
    public int SaasDefaultMaxConcurrentPerChannel { get; set; } = 3;

    /// <summary>SaaS instance-wide notification-dispatch concurrency — Postgres + Redis's real headroom.</summary>
    public int SaasMaxConcurrentNotificationDispatch { get; set; } = 64;
}
