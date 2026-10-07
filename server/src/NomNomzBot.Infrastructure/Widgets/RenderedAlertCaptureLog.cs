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

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// The per-broadcaster ring buffer of <c>RenderedAlertCapture</c> rows a later "Replay" re-broadcasts verbatim.
/// Every widget push that is a replayable alert records itself here, so the size window and the pruning live in
/// one place.
/// </summary>
public static class RenderedAlertCaptureLog
{
    // Same recency window the dashboard activity feed surfaces (DashboardController.GetActivity).
    public const int MaxCapturesPerBroadcaster = 40;

    private static readonly JsonSerializerOptions WireOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Standing state, not an alert: a value that is re-pushed on every change (custom data, the song-request
    /// queue, now playing). Capturing it would push real alerts out of the window and replay nothing useful.
    /// </summary>
    public static bool IsStandingState(string eventType) =>
        eventType == "sr_queue"
        || eventType == "now_playing"
        || eventType.StartsWith("custom.", StringComparison.Ordinal);

    /// <summary>
    /// Records <paramref name="data"/> in the hub's own wire form (camelCase, so a replay carries the names the
    /// live push carried), then prunes the broadcaster back down to <see cref="MaxCapturesPerBroadcaster"/>.
    /// </summary>
    public static async Task AppendAsync(
        IApplicationDbContext db,
        Guid broadcasterId,
        string eventType,
        object data,
        string? channelEventId,
        CancellationToken cancellationToken
    )
    {
        db.RenderedAlertCaptures.Add(
            new()
            {
                BroadcasterId = broadcasterId,
                EventType = eventType,
                Payload = JsonSerializer.Serialize(data, WireOptions),
                ChannelEventId = channelEventId,
            }
        );
        await db.SaveChangesAsync(cancellationToken);

        // CreatedAt can tie between rows written in the same tick; Id (MonotonicGuid) breaks the tie toward
        // insertion order, so the oldest row is always the one pruned.
        List<Guid> staleIds = await db
            .RenderedAlertCaptures.Where(c => c.BroadcasterId == broadcasterId)
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .Skip(MaxCapturesPerBroadcaster)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        if (staleIds.Count == 0)
            return;

        await db
            .RenderedAlertCaptures.Where(c => staleIds.Contains(c.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
