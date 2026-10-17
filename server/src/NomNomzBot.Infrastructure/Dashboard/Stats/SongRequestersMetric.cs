// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Stats;
using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Dashboard.Stats;

public sealed class SongRequestersMetric(IApplicationDbContext db)
    : StreamMetric<StreamStatsRankedDto>
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public override string Key => "songRequesters";

    protected override async Task<StreamStatsRankedDto> ComputeAsync(
        StreamWindow window,
        CancellationToken ct
    )
    {
        DateTime from = window.From;
        DateTime to = window.To;
        var rows = await db
            .Records.AsNoTracking()
            .Where(r =>
                r.BroadcasterId == window.ChannelId
                && r.RecordType == SongRequestHistory.RecordType
                && r.CreatedAt >= from
                && r.CreatedAt <= to
            )
            .Select(r => new { r.UserId, r.Data })
            .ToListAsync(ct);

        List<string> requesterIds = rows.Where(r => IsSong(r.Data)).Select(r => r.UserId).ToList();
        List<string> distinct = requesterIds.Distinct().ToList();
        Dictionary<string, string> names = await db
            .Users.AsNoTracking()
            .Where(u => u.TwitchUserId != null && distinct.Contains(u.TwitchUserId))
            .ToDictionaryAsync(u => u.TwitchUserId!, u => u.DisplayName, ct);

        return new()
        {
            Total = requesterIds.Count,
            Top = StreamRanking.TopOfNames(
                requesterIds.Select(id => names.GetValueOrDefault(id, id))
            ),
        };
    }

    private static bool IsSong(string data)
    {
        try
        {
            return JsonSerializer.Deserialize<SongRequestHistory>(data, CamelCase) is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    protected override void Assign(StreamStatsDto stats, StreamStatsRankedDto result) =>
        stats.SongRequesters = result;
}
