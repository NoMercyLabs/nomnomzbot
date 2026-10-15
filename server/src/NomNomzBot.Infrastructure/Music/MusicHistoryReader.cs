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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Reads the <c>SongRequest</c> rows <c>MusicService</c> writes on every accepted request. The requester name
/// is resolved from the user table by platform id; an unknown id falls back to the id itself so a row is never
/// dropped for lack of a profile.
/// </summary>
public sealed class MusicHistoryReader(IApplicationDbContext db) : IMusicHistoryReader
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<Result<List<SongHistoryItemDto>>> GetRecentAsync(
        Guid broadcasterId,
        int limit,
        CancellationToken cancellationToken = default
    )
    {
        var rows = await db
            .Records.AsNoTracking()
            .Where(r =>
                r.BroadcasterId == broadcasterId && r.RecordType == SongRequestHistory.RecordType
            )
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Take(limit)
            .Select(r => new
            {
                r.UserId,
                r.Data,
                r.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        Dictionary<string, string> names = await NamesAsync(
            rows.Select(r => r.UserId).Distinct().ToList(),
            cancellationToken
        );

        List<SongHistoryItemDto> items = [];
        foreach (var row in rows)
        {
            SongRequestHistory? track = Deserialize(row.Data);
            if (track is null)
                continue;

            items.Add(
                new SongHistoryItemDto(
                    track.TrackName,
                    track.Artist,
                    track.ImageUrl,
                    track.Provider,
                    row.UserId,
                    names.GetValueOrDefault(row.UserId, row.UserId),
                    row.CreatedAt
                )
            );
        }

        return Result.Success(items);
    }

    public async Task<Result<List<TopRequesterDto>>> GetTopRequestersAsync(
        Guid broadcasterId,
        int limit,
        CancellationToken cancellationToken = default
    )
    {
        var counts = await db
            .Records.AsNoTracking()
            .Where(r =>
                r.BroadcasterId == broadcasterId && r.RecordType == SongRequestHistory.RecordType
            )
            .GroupBy(r => r.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.UserId)
            .Take(limit)
            .ToListAsync(cancellationToken);

        Dictionary<string, string> names = await NamesAsync(
            counts.Select(c => c.UserId).ToList(),
            cancellationToken
        );

        List<TopRequesterDto> top =
        [
            .. counts.Select(c => new TopRequesterDto(
                c.UserId,
                names.GetValueOrDefault(c.UserId, c.UserId),
                c.Count
            )),
        ];
        return Result.Success(top);
    }

    private async Task<Dictionary<string, string>> NamesAsync(
        List<string> platformIds,
        CancellationToken cancellationToken
    ) =>
        await db
            .Users.AsNoTracking()
            .Where(u => u.TwitchUserId != null && platformIds.Contains(u.TwitchUserId))
            .ToDictionaryAsync(u => u.TwitchUserId!, u => u.DisplayName, cancellationToken);

    private static SongRequestHistory? Deserialize(string data)
    {
        try
        {
            return JsonSerializer.Deserialize<SongRequestHistory>(data, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
