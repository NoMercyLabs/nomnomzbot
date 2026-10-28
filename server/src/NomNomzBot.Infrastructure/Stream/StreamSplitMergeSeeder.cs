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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Services;
using NomNomzBot.Domain.Analytics.Entities;
using StreamEntity = NomNomzBot.Domain.Stream.Entities.Stream;

namespace NomNomzBot.Infrastructure.Stream;

/// <summary>
/// Merges one real stream that was stored as two rows (the import, or a dropped connection, split a 24 hour
/// stream into 22:57 to 06:21 and 06:22 to 23:01). Two consecutive rows of a channel are one stream when the
/// second starts less than <see cref="MaxGap"/> from the first one's end (before or after it) and the channel
/// shows life on both sides of the seam: a chat line or a journaled viewer-count sample inside
/// <see cref="ProofWindow"/> before the first row's end AND inside it after the second row's start. A gap
/// without that proof is a real break and stays two streams; rows with the same start are duplicates, which
/// <c>StreamRecapBackfillSeeder</c> already skips. The first row keeps its id and start and takes the later
/// end; chat lines, voice segments, the chatter-day pointer and watch sessions of the second row move to it
/// (a viewer present in both gets one session with the summed time and lines), the second row's recap and the
/// row itself are removed (the Streams table has no soft-delete column, so the row is deleted after everything
/// that pointed at it moved), and the merged recap is rebuilt. Viewer-count samples are journal events keyed
/// by time, so they already belong to the merged window. Idempotent: a merged pair is one row and no longer
/// matches. It runs after <c>StreamEndRepairSeeder</c> (940), so the ends are final, and before the recap
/// backfill (950).
/// </summary>
public sealed class StreamSplitMergeSeeder(
    IApplicationDbContext db,
    IStreamRecapService recaps,
    ILogger<StreamSplitMergeSeeder> logger
) : ISeeder
{
    public int Order => 945;

    private static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan ProofWindow = TimeSpan.FromMinutes(10);

    public async Task SeedAsync(CancellationToken ct = default)
    {
        List<StreamEntity> rows = await db
            .Streams.IgnoreQueryFilters()
            .Where(s => s.StartedAt != null && s.EndedAt != null)
            .OrderBy(s => s.ChannelId)
            .ThenBy(s => s.StartedAt)
            .ToListAsync(ct);

        List<StreamEntity> merged = [];
        StreamEntity? head = null;
        foreach (StreamEntity row in rows)
        {
            if (
                head is null
                || head.ChannelId != row.ChannelId
                || !await IsSplitAsync(head, row, ct)
            )
            {
                head = row;
                continue;
            }

            await FoldAsync(head, row, ct);
            if (!merged.Contains(head))
                merged.Add(head);
        }

        if (merged.Count == 0)
            return;

        await db.SaveChangesAsync(ct);
        logger.LogWarning(
            "Merged {Count} stream(s) that were stored as two rows with a short gap",
            merged.Count
        );

        foreach (StreamEntity stream in merged)
        {
            Result<StreamRecapDto> rebuilt = await recaps.BuildAsync(
                stream.ChannelId,
                stream.Id,
                null,
                ct
            );
            if (rebuilt.IsFailure)
                logger.LogWarning(
                    "The recap of merged stream {StreamId} was not rebuilt: {Error}",
                    stream.Id,
                    rebuilt.ErrorMessage
                );
        }
    }

    private async Task<bool> IsSplitAsync(
        StreamEntity head,
        StreamEntity next,
        CancellationToken ct
    )
    {
        DateTimeOffset seam = head.EndedAt!.Value;
        DateTimeOffset restart = next.StartedAt!.Value;
        if (restart <= head.StartedAt)
            return false;

        TimeSpan gap = restart - seam;
        if (gap >= MaxGap || gap <= -MaxGap)
            return false;

        return await HasLifeAsync(head.ChannelId, seam - ProofWindow, seam.AddTicks(1), ct)
            && await HasLifeAsync(head.ChannelId, restart, restart + ProofWindow, ct);
    }

    private async Task<bool> HasLifeAsync(
        Guid channelId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct
    )
    {
        DateTime start = from.UtcDateTime;
        DateTime end = to.UtcDateTime;

        bool chat = await db
            .ChatMessages.IgnoreQueryFilters()
            .AnyAsync(
                m => m.BroadcasterId == channelId && m.CreatedAt >= start && m.CreatedAt < end,
                ct
            );
        if (chat)
            return true;

        return await db.EventJournals.AnyAsync(
            j =>
                j.BroadcasterId == channelId
                && j.EventType == StreamRowReconciler.ViewerSampleEventType
                && j.OccurredAt >= start
                && j.OccurredAt < end,
            ct
        );
    }

    private async Task FoldAsync(StreamEntity head, StreamEntity next, CancellationToken ct)
    {
        string headId = head.Id;
        string nextId = next.Id;

        if (next.EndedAt > head.EndedAt)
            head.EndedAt = next.EndedAt;

        await db
            .ChatMessages.IgnoreQueryFilters()
            .Where(m => m.StreamId == nextId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.StreamId, headId), ct);
        await db
            .VoiceTranscriptSegments.IgnoreQueryFilters()
            .Where(v => v.StreamId == nextId)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.StreamId, headId), ct);
        await db
            .ChannelChatterDays.IgnoreQueryFilters()
            .Where(d => d.LastStreamId == nextId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.LastStreamId, headId), ct);
        await db
            .StreamRecaps.IgnoreQueryFilters()
            .Where(r => r.StreamId == nextId)
            .ExecuteDeleteAsync(ct);

        await FoldWatchSessionsAsync(headId, nextId, ct);
        db.Streams.Remove(next);
    }

    private async Task FoldWatchSessionsAsync(string headId, string nextId, CancellationToken ct)
    {
        List<WatchSession> kept = await db
            .WatchSessions.IgnoreQueryFilters()
            .Where(w => w.StreamId == headId)
            .ToListAsync(ct);
        List<WatchSession> moving = await db
            .WatchSessions.IgnoreQueryFilters()
            .Where(w => w.StreamId == nextId)
            .ToListAsync(ct);

        foreach (WatchSession session in moving)
        {
            WatchSession? same = kept.Find(k => k.ViewerUserId == session.ViewerUserId);
            if (same is null)
            {
                session.StreamId = headId;
                continue;
            }

            same.StartedAt =
                session.StartedAt < same.StartedAt ? session.StartedAt : same.StartedAt;
            same.EndedAt = Later(same.EndedAt, session.EndedAt);
            same.DurationSeconds += session.DurationSeconds;
            same.MessageCountInSession += session.MessageCountInSession;
            same.PresenceConfirmed |= session.PresenceConfirmed;
            db.WatchSessions.Remove(session);
        }
    }

    private static DateTime? Later(DateTime? a, DateTime? b) =>
        a is null || b is null ? null
        : a > b ? a
        : b;
}
