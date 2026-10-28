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

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Stats;
using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Dashboard.Stats;

public sealed class SongRequestersMetric(IApplicationDbContext db, StreamBanFilter bans)
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
        BannedPeople banned = await bans.ForChannelAsync(window.ChannelId, ct);
        var rows = await db
            .Records.AsNoTracking()
            .WithoutBanned(banned)
            .Where(r =>
                r.BroadcasterId == window.ChannelId
                && r.RecordType == SongRequestHistory.RecordType
                && r.DeletedAt == null
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
