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
    /// Closes every open Stream row of an offline channel. The end is the latest chat message stored
    /// against that stream (the last proof the stream was running) when there is one, else
    /// <paramref name="observedAt"/> — the moment the bot saw the channel offline. The newest closed row also
    /// gets its empty title and category filled from the channel's stored values. Returns true when a row changed.
    /// </summary>
    internal static async Task<bool> CloseOpenStreamsAsync(
        IApplicationDbContext db,
        Channel channel,
        DateTimeOffset observedAt,
        CancellationToken ct
    )
    {
        List<Domain.Stream.Entities.Stream> open = await db
            .Streams.Where(s => s.ChannelId == channel.Id && s.EndedAt == null)
            .OrderByDescending(s => s.StartedAt)
            .ToListAsync(ct);
        if (open.Count == 0)
            return false;

        foreach (Domain.Stream.Entities.Stream row in open)
        {
            // Tracked and already closed by the caller in this unit of work (exact end): keep it.
            if (row.EndedAt is not null)
                continue;

            DateTime? lastChat = await db
                .ChatMessages.Where(m => m.StreamId == row.Id)
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => (DateTime?)m.CreatedAt)
                .FirstOrDefaultAsync(ct);
            row.EndedAt = EndOf(row, lastChat, observedAt);
        }

        BackfillMetadata(open[0], channel);
        return true;
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

    private static DateTimeOffset EndOf(
        Domain.Stream.Entities.Stream row,
        DateTime? lastChat,
        DateTimeOffset observedAt
    )
    {
        DateTimeOffset end = observedAt;
        if (lastChat is { } chat)
        {
            DateTimeOffset chatAt = new(DateTime.SpecifyKind(chat, DateTimeKind.Utc));
            if (chatAt < end)
                end = chatAt;
        }
        return row.StartedAt is { } start && end < start ? start : end;
    }
}
