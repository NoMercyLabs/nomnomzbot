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
