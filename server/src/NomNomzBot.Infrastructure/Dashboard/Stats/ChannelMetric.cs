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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Stats;

namespace NomNomzBot.Infrastructure.Dashboard.Stats;

/// <summary>The streaming channel's display name and profile picture, read from the channel's own account.</summary>
public sealed class ChannelMetric(IApplicationDbContext db) : StreamMetric<StreamStatsChannelDto?>
{
    public override string Key => "channel";

    protected override async Task<StreamStatsChannelDto?> ComputeAsync(
        StreamWindow window,
        CancellationToken ct
    ) =>
        await db
            .Channels.AsNoTracking()
            .Where(c => c.Id == window.ChannelId)
            .Select(c => new StreamStatsChannelDto
            {
                Name = c.User.DisplayName,
                AvatarUrl = c.User.ProfileImageUrl,
            })
            .FirstOrDefaultAsync(ct);

    protected override void Assign(StreamStatsDto stats, StreamStatsChannelDto? result) =>
        stats.Channel = result;
}
