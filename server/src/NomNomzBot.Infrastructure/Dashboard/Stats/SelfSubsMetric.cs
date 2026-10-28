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

using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Stats;

namespace NomNomzBot.Infrastructure.Dashboard.Stats;

/// <summary>People who subscribed or resubscribed themselves, oldest first; gift recipients are excluded.</summary>
public sealed class SelfSubsMetric(StreamFactReader facts) : StreamMetric<List<string>>
{
    public override string Key => "selfSubs";

    protected override async Task<List<string>> ComputeAsync(
        StreamWindow window,
        CancellationToken ct
    )
    {
        List<StreamFact> fresh = await facts.OfTypeAsync(window, "channel.subscribe", ct);
        List<StreamFact> resubs = await facts.OfTypeAsync(
            window,
            "channel.subscription.message",
            ct
        );
        return fresh
            .Where(f => !f.Flag("isGift"))
            .Concat(resubs)
            .OrderBy(f => f.OccurredAt())
            .Select(f => f.Text("userDisplayName"))
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .Take(StreamRanking.ListCap)
            .ToList();
    }

    protected override void Assign(StreamStatsDto stats, List<string> result) =>
        stats.SelfSubs = result;
}
