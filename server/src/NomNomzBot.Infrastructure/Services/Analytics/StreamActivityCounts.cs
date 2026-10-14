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

namespace NomNomzBot.Infrastructure.Services.Analytics;

/// <summary>One stream's activity window: start to end (or "now" while the stream is live).</summary>
internal readonly record struct StreamWindow(string StreamId, DateTime Start, DateTime End);

/// <summary>A stream's chat lines and distinct chatters inside its window.</summary>
internal readonly record struct StreamChatCounts(long TotalMessages, int UniqueChatters);

/// <summary>
/// Window-folded chat and follow counts for many streams in ONE query per metric (a UNION of keyed window
/// slices, grouped by stream) instead of a query per stream. The single-stream view calls it with one window,
/// so the list and the detail count the same way.
/// </summary>
internal static class StreamActivityCounts
{
    public const string FollowEventType = "channel.follow";

    public static async Task<Dictionary<string, StreamChatCounts>> ChatByStreamAsync(
        IApplicationDbContext db,
        Guid broadcasterId,
        IReadOnlyList<StreamWindow> windows,
        CancellationToken ct
    )
    {
        IQueryable<StreamUser>? keyed = null;
        foreach (StreamWindow w in windows)
        {
            string streamId = w.StreamId;
            DateTime start = w.Start;
            DateTime end = w.End;
            IQueryable<StreamUser> slice = db
                .ChatMessages.Where(m =>
                    m.BroadcasterId == broadcasterId && m.CreatedAt >= start && m.CreatedAt < end
                )
                .Select(m => new StreamUser { StreamId = streamId, UserId = m.UserId });
            keyed = keyed is null ? slice : keyed.Concat(slice);
        }

        if (keyed is null)
            return [];

        return await keyed
            .GroupBy(r => r.StreamId)
            .Select(g => new
            {
                StreamId = g.Key,
                Total = g.LongCount(),
                Chatters = g.Select(r => r.UserId).Distinct().Count(),
            })
            .ToDictionaryAsync(g => g.StreamId, g => new StreamChatCounts(g.Total, g.Chatters), ct);
    }

    public static async Task<Dictionary<string, int>> FollowsByStreamAsync(
        IApplicationDbContext db,
        Guid broadcasterId,
        IReadOnlyList<StreamWindow> windows,
        CancellationToken ct
    )
    {
        IQueryable<StreamUser>? keyed = null;
        foreach (StreamWindow w in windows)
        {
            string streamId = w.StreamId;
            DateTime start = w.Start;
            DateTime end = w.End;
            IQueryable<StreamUser> slice = db
                .ChannelEvents.Where(e =>
                    e.ChannelId == broadcasterId
                    && e.Type == FollowEventType
                    && e.CreatedAt >= start
                    && e.CreatedAt < end
                )
                .Select(e => new StreamUser { StreamId = streamId, UserId = e.Type });
            keyed = keyed is null ? slice : keyed.Concat(slice);
        }

        if (keyed is null)
            return [];

        return await keyed
            .GroupBy(r => r.StreamId)
            .Select(g => new { StreamId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.StreamId, g => g.Count, ct);
    }

    private sealed class StreamUser
    {
        public string StreamId { get; init; } = null!;
        public string UserId { get; init; } = null!;
    }
}
