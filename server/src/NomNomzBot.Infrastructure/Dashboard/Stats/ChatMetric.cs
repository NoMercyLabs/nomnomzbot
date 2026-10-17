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

public sealed class ChatMetric(IApplicationDbContext db) : StreamMetric<StreamStatsChatDto>
{
    public override string Key => "chat";

    protected override async Task<StreamStatsChatDto> ComputeAsync(
        StreamWindow window,
        CancellationToken ct
    )
    {
        List<string> userIds = await db
            .ChatMessages.AsNoTracking()
            .Where(m => m.BroadcasterId == window.ChannelId && m.StreamId == window.StreamId)
            .Select(m => m.UserId)
            .ToListAsync(ct);
        return new() { Messages = userIds.Count, UniqueChatters = userIds.Distinct().Count() };
    }

    protected override void Assign(StreamStatsDto stats, StreamStatsChatDto result) =>
        stats.Chat = result;
}
