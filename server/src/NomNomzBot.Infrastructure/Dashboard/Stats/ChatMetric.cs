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

public sealed class ChatMetric(IApplicationDbContext db, StreamBanFilter bans)
    : StreamMetric<StreamStatsChatDto>
{
    public override string Key => "chat";

    protected override async Task<StreamStatsChatDto> ComputeAsync(
        StreamWindow window,
        CancellationToken ct
    )
    {
        BannedPeople banned = await bans.ForChannelAsync(window.ChannelId, ct);
        List<string> userIds = await db
            .ChatMessages.AsNoTracking()
            .InWindow(window)
            .WithoutBanned(banned)
            .Select(m => m.UserId)
            .ToListAsync(ct);
        return new() { Messages = userIds.Count, UniqueChatters = userIds.Distinct().Count() };
    }

    protected override void Assign(StreamStatsDto stats, StreamStatsChatDto result) =>
        stats.Chat = result;
}
