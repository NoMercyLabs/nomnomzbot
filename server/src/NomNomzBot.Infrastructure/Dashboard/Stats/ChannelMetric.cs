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
