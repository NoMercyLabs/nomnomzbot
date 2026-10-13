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
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Stream;

/// <summary>
/// The one place that reconciles a channel's <c>Streams</c> rows with what the bot has learned about the
/// channel's live state. Callers: the offline handler, the poller (every offline observation, not only a
/// live-to-offline edge held in memory) and the startup bootstrap. None of these save; the caller saves once.
/// </summary>
internal static class StreamRowReconciler
{
    /// <summary>
    /// Closes every open Stream row of a channel the bot knows is not streaming, but only where the end is
    /// provable: the latest chat message stored against that stream, else the latest chat message of the
    /// channel inside the row's window (its <c>StartedAt</c> up to the next stream row's <c>StartedAt</c>, or
    /// <paramref name="upperBound"/> when no later row exists). With no such chat the row stays open — the
    /// bot never invents a stream length. The newest open row also gets its empty title and category
    /// filled from the channel's stored values (<paramref name="backfillText"/>; the online handler passes
    /// false because the channel's stored text then belongs to the NEW stream). Returns true when a row changed.
    /// </summary>
    internal static async Task<bool> CloseOpenStreamsAsync(
        IApplicationDbContext db,
        Channel channel,
        DateTimeOffset upperBound,
        CancellationToken ct,
        bool backfillText = true
    )
    {
        List<Domain.Stream.Entities.Stream> open = await db
            .Streams.Where(s => s.ChannelId == channel.Id && s.EndedAt == null)
            .ToListAsync(ct);
        if (open.Count == 0)
            return false;
        open = open.OrderByDescending(s => s.StartedAt).ToList();

        List<DateTimeOffset?> starts = await db
            .Streams.Where(s => s.ChannelId == channel.Id)
            .Select(s => s.StartedAt)
            .ToListAsync(ct);

        bool changed = false;
        foreach (Domain.Stream.Entities.Stream row in open)
        {
            // Tracked and already closed by the caller in this unit of work (exact end): keep it.
            if (row.EndedAt is not null)
                continue;

            DateTimeOffset? end = await ProvableEndAsync(
                db,
                channel.Id,
                row,
                starts,
                upperBound,
                ct
            );
            if (end is null)
                continue;
            row.EndedAt = end;
            changed = true;
        }

        if (backfillText && BackfillMetadata(open[0], channel))
            changed = true;
        return changed;
    }

    private static async Task<DateTimeOffset?> ProvableEndAsync(
        IApplicationDbContext db,
        Guid channelId,
        Domain.Stream.Entities.Stream row,
        List<DateTimeOffset?> starts,
        DateTimeOffset upperBound,
        CancellationToken ct
    )
    {
        DateTime? lastChat = await db
            .ChatMessages.Where(m => m.StreamId == row.Id)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => (DateTime?)m.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (lastChat is null && row.StartedAt is { } start)
        {
            DateTimeOffset next = starts
                .Where(s => s > start)
                .Select(s => s!.Value)
                .DefaultIfEmpty(upperBound)
                .Min();
            if (next > upperBound)
                next = upperBound;
            DateTime from = start.UtcDateTime;
            DateTime to = next.UtcDateTime;
            lastChat = await db
                .ChatMessages.Where(m =>
                    m.BroadcasterId == channelId && m.CreatedAt >= from && m.CreatedAt < to
                )
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => (DateTime?)m.CreatedAt)
                .FirstOrDefaultAsync(ct);
        }

        return lastChat is { } chat
            ? new DateTimeOffset(DateTime.SpecifyKind(chat, DateTimeKind.Utc))
            : null;
    }

    /// <summary>
    /// Fills an empty title / category on the row from the channel's stored values. Never overwrites text the
    /// row already has and never writes an empty value. Returns true when a field changed.
    /// </summary>
    internal static bool BackfillMetadata(Domain.Stream.Entities.Stream row, Channel channel)
    {
        bool changed = false;
        if (string.IsNullOrEmpty(row.Title) && !string.IsNullOrEmpty(channel.Title))
        {
            row.Title = channel.Title;
            changed = true;
        }
        if (string.IsNullOrEmpty(row.GameName) && !string.IsNullOrEmpty(channel.GameName))
        {
            row.GameName = channel.GameName;
            changed = true;
        }
        return changed;
    }
}
