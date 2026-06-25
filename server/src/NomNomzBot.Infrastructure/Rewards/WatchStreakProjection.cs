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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Rewards.Entities;

namespace NomNomzBot.Infrastructure.Rewards;

/// <summary>
/// Folds <c>WatchStreakReceivedEvent</c> into <see cref="WatchStreak"/> rows: upserts by
/// (BroadcasterId, UserId), advances <see cref="WatchStreak.CurrentStreak"/> and keeps
/// <see cref="WatchStreak.MaxStreak"/> at the high-water mark.
/// </summary>
public sealed class WatchStreakProjection(IApplicationDbContext db) : IProjection
{
    private static readonly HashSet<string> Subscribed = new(StringComparer.Ordinal)
    {
        "WatchStreakReceivedEvent",
    };

    public string Name => "watch-streak";
    public bool IsGlobal => false;
    public IReadOnlySet<string> SubscribedEventTypes => Subscribed;

    public async Task<Result> ApplyAsync(
        EventRecord @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId is not Guid broadcasterId)
            return Result.Success();

        JObject? payload = TryParse(@event.PayloadJson);
        if (payload is null)
            return Result.Success();

        string? userId = payload["UserId"]?.Value<string>();
        int? streakMonths = payload["StreakMonths"]?.Value<int?>();

        if (string.IsNullOrEmpty(userId) || streakMonths is null)
            return Result.Success();

        string? displayName = payload["UserDisplayName"]?.Value<string>();
        DateOnly eventDate = DateOnly.FromDateTime(@event.OccurredAt);

        WatchStreak? row = await db.WatchStreaks.FirstOrDefaultAsync(
            w => w.BroadcasterId == broadcasterId && w.UserId == userId,
            cancellationToken
        );

        if (row is null)
        {
            row = new WatchStreak
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = broadcasterId,
                UserId = userId,
            };
            await db.WatchStreaks.AddAsync(row, cancellationToken);
        }

        row.CurrentStreak = streakMonths.Value;
        if (streakMonths.Value > row.MaxStreak)
            row.MaxStreak = streakMonths.Value;

        row.UserDisplayName = displayName ?? row.UserDisplayName;
        row.LastSeenDate = eventDate;

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ResetAsync(
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<WatchStreak> rows = await (
            broadcasterId is Guid id
                ? db.WatchStreaks.Where(w => w.BroadcasterId == id)
                : db.WatchStreaks
        ).ToListAsync(cancellationToken);

        db.WatchStreaks.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static JObject? TryParse(string json)
    {
        try
        {
            return JObject.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
