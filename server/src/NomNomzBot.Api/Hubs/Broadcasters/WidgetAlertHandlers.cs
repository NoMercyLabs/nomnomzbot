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
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Fans a channel domain event out to the overlay widgets (OBS browser-sources) that subscribe to it — the link
/// from a domain event to an on-stream alert over <c>OverlayHub</c>. The routing decision lives in
/// <see cref="WidgetAlertRouting"/>; this is the shared db-read + push. The transient user-facing alerts
/// (follow/sub/cheer/raid/gift/resub/reward/role/shoutout/ban/hype-train) route through this via
/// <see cref="OverlayAlertBroadcast"/>, which pushes the SAME decorated dto the dashboard gets — so a widget never
/// sees a thinner payload than the dashboard. Standing displays with no dashboard-enriched equivalent (now-playing)
/// keep their own handler below.
///
/// This is also the SINGLE choke point that every one of those pushes goes through, so it is where a verbatim
/// <see cref="RenderedAlertCapture"/> row is written — the foundation for a later "Replay" action on the dashboard
/// activity feed (S-REPLAY-ENDPOINT): re-broadcasting the captured payload byte-for-byte, never re-deriving it,
/// so replay can never double-run a persistent side effect (currency grants, loyalty points, reward fulfillment)
/// that already ran once when the origin event fired.
/// </summary>
internal static class WidgetAlertDispatch
{
    // Same recency window the dashboard activity feed surfaces (DashboardController.GetActivity) — captures
    // beyond it can never be replayed from the feed, so keeping them would only grow the table unbounded.
    private const int MaxCapturesPerBroadcaster = 40;

    public static async Task RouteAsync(
        IApplicationDbContext db,
        IWidgetNotifier notifier,
        Guid broadcasterId,
        string eventType,
        object data,
        CancellationToken cancellationToken
    )
    {
        if (broadcasterId == Guid.Empty)
            return;

        List<Widget> widgets = await db
            .Widgets.Where(w => w.BroadcasterId == broadcasterId)
            .ToListAsync(cancellationToken);

        List<Widget> subscribers = WidgetAlertRouting.Subscribers(widgets, eventType).ToList();
        if (subscribers.Count > 0)
            await CaptureAsync(db, broadcasterId, eventType, data, cancellationToken);

        foreach (Widget widget in subscribers)
            await notifier.SendWidgetEventAsync(
                broadcasterId.ToString(),
                widget.Id.ToString(),
                new(widget.Id.ToString(), eventType, data),
                cancellationToken
            );
    }

    /// <summary>
    /// Records the exact <paramref name="data"/> object pushed for <paramref name="eventType"/>, then prunes
    /// this broadcaster's captures back down to <see cref="MaxCapturesPerBroadcaster"/> — a simple
    /// prune-on-write rather than a background job, since there is no precedent for one over a bounded log
    /// this small in this codebase.
    /// </summary>
    private static async Task CaptureAsync(
        IApplicationDbContext db,
        Guid broadcasterId,
        string eventType,
        object data,
        CancellationToken cancellationToken
    )
    {
        db.RenderedAlertCaptures.Add(
            new()
            {
                BroadcasterId = broadcasterId,
                EventType = eventType,
                Payload = JsonSerializer.Serialize(data),
            }
        );
        await db.SaveChangesAsync(cancellationToken);

        // CreatedAt is stamped by AuditableEntityInterceptor at SaveChanges time and can tie between rows
        // written in the same tick (test fakes with no interceptor tie on every row); Id (Guid.CreateVersion7,
        // time-ordered) breaks the tie deterministically toward "insertion order", so the oldest row is always
        // the one pruned.
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

/// <summary>
/// Playback change → the persistent <c>now_playing</c> overlay widget (music-sr.md). Unlike the transient alerts,
/// this drives a standing now-playing display the browser-source keeps on screen until the next change, and it has
/// no richer dashboard-enriched equivalent (the dashboard music-state push carries the same track name), so it keeps
/// its own flattened handler rather than routing through <see cref="OverlayAlertBroadcast"/>.
/// </summary>
public sealed class WidgetNowPlayingHandler(IApplicationDbContext db, IWidgetNotifier notifier)
    : IEventHandler<PlaybackStateChangedEvent>
{
    public Task HandleAsync(
        PlaybackStateChangedEvent @event,
        CancellationToken cancellationToken = default
    ) =>
        WidgetAlertDispatch.RouteAsync(
            db,
            notifier,
            @event.BroadcasterId,
            "now_playing",
            new
            {
                isPlaying = @event.IsPlaying,
                track = @event.TrackName,
                artist = @event.Artist,
                artUrl = @event.AlbumArtUrl,
                provider = @event.Provider,
                trackUri = @event.TrackUri,
                durationMs = @event.DurationMs,
                progressMs = @event.ProgressMs,
                observedAt = @event.ObservedAt,
            },
            cancellationToken
        );
}

/// <summary>
/// A track saved/unsaved (liked/unliked) → the <c>now_playing</c> overlay's heart-pulse animation. Transient,
/// unlike the standing <see cref="WidgetNowPlayingHandler"/> snapshot — fired once per like/unlike, not
/// re-sent on every reload.
/// </summary>
public sealed class WidgetTrackSavedHandler(IApplicationDbContext db, IWidgetNotifier notifier)
    : IEventHandler<TrackSavedChangedEvent>
{
    public Task HandleAsync(
        TrackSavedChangedEvent @event,
        CancellationToken cancellationToken = default
    ) =>
        WidgetAlertDispatch.RouteAsync(
            db,
            notifier,
            @event.BroadcasterId,
            "track_saved_changed",
            new
            {
                trackUri = @event.TrackUri,
                track = @event.TrackName,
                artist = @event.Artist,
                isSaved = @event.IsSaved,
            },
            cancellationToken
        );
}
