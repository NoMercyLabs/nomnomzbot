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

public sealed class SubsMetric(StreamFactReader facts) : StreamMetric<StreamStatsSubsDto>
{
    public override string Key => "subs";

    protected override async Task<StreamStatsSubsDto> ComputeAsync(
        StreamWindow window,
        CancellationToken ct
    )
    {
        int fresh = (await facts.OfTypeAsync(window, "channel.subscribe", ct)).Count(f =>
            !f.Flag("isGift")
        );
        int resub = (await facts.OfTypeAsync(window, "channel.subscription.message", ct)).Count;
        int gift = (await facts.OfTypeAsync(window, "channel.subscription.gift", ct)).Sum(f =>
            f.Int("giftCount")
        );
        return new()
        {
            New = fresh,
            Resub = resub,
            Gift = gift,
            Total = fresh + resub + gift,
        };
    }

    protected override void Assign(StreamStatsDto stats, StreamStatsSubsDto result) =>
        stats.Subs = result;
}
