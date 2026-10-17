// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
