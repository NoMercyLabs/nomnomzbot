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
    internal const string ViewerSampleEventType = "StreamViewerCountSampledEvent";

    private static readonly TimeSpan MaxChatSilence = TimeSpan.FromHours(6);

    /// <summary>
    /// Closes every open Stream row of a channel the bot knows is not streaming, but only where the end is
    /// provable: the latest chat message stored against that stream, else the latest chat message of the
    /// channel inside the row's window (its <c>StartedAt</c> up to the next stream row's <c>StartedAt</c>, or
    /// <paramref name="upperBound"/> when no later row exists). With no such chat the row ends at its own
    /// start — the bot never invents a stream length, and an open row would count as live. The newest open row also gets its empty title and category
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
            // No chat proves a later end: the start is the only proven moment. An open row would count as live.
            end ??= row.StartedAt;
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
        if (row.StartedAt is not { } start)
            return null;

        DateTimeOffset next = starts
            .Where(s => s > start)
            .Select(s => s!.Value)
            .DefaultIfEmpty(upperBound)
            .Min();
        if (next > upperBound)
            next = upperBound;

        return await LastActivityAsync(db, channelId, row, next, ct);
    }

    /// <summary>
    /// The last proof that a stream was live: the latest of the chat stored against the row, the channel's
    /// chat inside the row's window (<c>StartedAt</c> up to <paramref name="windowEnd"/>, exclusive) and the
    /// journaled viewer-count samples inside that window (the poll publishes a sample only while the channel
    /// is live). Null when none exists. This is the one definition of "the end of a stream whose offline
    /// event never came"; it never returns the next stream's start.
    /// </summary>
    internal static async Task<DateTimeOffset?> LastActivityAsync(
        IApplicationDbContext db,
        Guid channelId,
        Domain.Stream.Entities.Stream row,
        DateTimeOffset windowEnd,
        CancellationToken ct
    )
    {
        if (row.StartedAt is not { } start)
            return null;

        DateTime from = start.UtcDateTime;
        DateTime to = windowEnd.UtcDateTime;

        // Chat is weaker proof than a sample: viewers keep talking after the stream ends, up to the next start.
        DateTime? lastSample = await LastSampleUtcAsync(db, channelId, from, to, ct);
        DateTime? latest =
            lastSample ?? await LastChatOfFirstRunAsync(db, channelId, row, from, to, ct);
        return latest is { } at
            ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc))
            : null;
    }

    /// <summary>
    /// The newest journaled viewer-count sample of the channel inside the row's window, or null. A sample is
    /// published only while Twitch reports the channel live, so it is the strongest proof of live time.
    /// </summary>
    internal static async Task<DateTimeOffset?> LastSampleAsync(
        IApplicationDbContext db,
        Guid channelId,
        Domain.Stream.Entities.Stream row,
        DateTimeOffset windowEnd,
        CancellationToken ct
    )
    {
        if (row.StartedAt is not { } start)
            return null;

        DateTime? at = await LastSampleUtcAsync(
            db,
            channelId,
            start.UtcDateTime,
            windowEnd.UtcDateTime,
            ct
        );
        return at is { } sample
            ? new DateTimeOffset(DateTime.SpecifyKind(sample, DateTimeKind.Utc))
            : null;
    }

    private static Task<DateTime?> LastSampleUtcAsync(
        IApplicationDbContext db,
        Guid channelId,
        DateTime from,
        DateTime to,
        CancellationToken ct
    ) =>
        db
            .EventJournals.Where(j =>
                j.BroadcasterId == channelId
                && j.EventType == ViewerSampleEventType
                && j.OccurredAt >= from
                && j.OccurredAt < to
            )
            .OrderByDescending(j => j.OccurredAt)
            .Select(j => (DateTime?)j.OccurredAt)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// The last chat message of the first unbroken run of chat that starts the stream: the run ends at the first
    /// silence longer than <see cref="MaxChatSilence"/>, because chat after that is the channel talking while
    /// offline, not the stream.
    /// </summary>
    private static async Task<DateTime?> LastChatOfFirstRunAsync(
        IApplicationDbContext db,
        Guid channelId,
        Domain.Stream.Entities.Stream row,
        DateTime from,
        DateTime to,
        CancellationToken ct
    )
    {
        List<DateTime> times = await db
            .ChatMessages.Where(m =>
                (m.BroadcasterId == channelId && m.CreatedAt >= from && m.CreatedAt < to)
                || m.StreamId == row.Id
            )
            .Select(m => m.CreatedAt)
            .ToListAsync(ct);
        times.Sort();

        DateTime? last = null;
        foreach (DateTime at in times)
        {
            if (last is { } previous && at - previous > MaxChatSilence)
                break;
            last = at;
        }

        return last;
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
