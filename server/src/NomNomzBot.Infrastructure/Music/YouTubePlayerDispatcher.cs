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
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>Finds the channel's open YouTube player pages and tells them what to play.</summary>
public interface IYouTubePlayerDispatcher
{
    /// <summary>
    /// The one player widget that owns the channel's YouTube audio, as a list of zero or one id. Candidates are
    /// the enabled widgets that subscribe <c>now_playing</c> or <c>youtube.play</c> and have a page open. The
    /// owner is the candidate whose report the store accepted last, else the earliest created.
    /// </summary>
    Task<IReadOnlyList<Guid>> FindPlayersAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Sends <c>youtube.play</c> for the video to the given player widgets.</summary>
    Task PlayAsync(
        Guid broadcasterId,
        IReadOnlyList<Guid> players,
        string videoId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Sends <c>youtube.pause</c> to the given player widgets.</summary>
    Task PauseAsync(
        Guid broadcasterId,
        IReadOnlyList<Guid> players,
        CancellationToken cancellationToken = default
    );

    /// <summary>Sends <c>youtube.resume</c> to the given player widgets.</summary>
    Task ResumeAsync(
        Guid broadcasterId,
        IReadOnlyList<Guid> players,
        CancellationToken cancellationToken = default
    );

    /// <summary>Sends <c>youtube.seek</c> with the position in milliseconds to the given player widgets.</summary>
    Task SeekAsync(
        Guid broadcasterId,
        IReadOnlyList<Guid> players,
        long positionMs,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Hands the next queued request to the owning player, as an ended video does. With no player open the
    /// request stays queued for the next page that attaches; with nothing queued nothing is sent and the
    /// result is false.
    /// </summary>
    Task<bool> HandOverNextAsync(Guid broadcasterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends <c>youtube.stop</c> to the owning player, which stops the video and reports it ENDED. With no
    /// player open nothing is sent.
    /// </summary>
    Task StopAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
}

public sealed class YouTubePlayerDispatcher(
    IApplicationDbContext db,
    IOverlayPresenceRegistry presence,
    IWidgetEventNotifier notifier,
    IYouTubePlayerStateStore store
) : IYouTubePlayerDispatcher
{
    public const string PlayEventType = "youtube.play";
    public const string PauseEventType = "youtube.pause";
    public const string ResumeEventType = "youtube.resume";
    public const string StopEventType = "youtube.stop";
    public const string SeekEventType = "youtube.seek";
    private const string NowPlayingEventType = "now_playing";

    public async Task<IReadOnlyList<Guid>> FindPlayersAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<Widget> widgets = await db
            .Widgets.AsNoTracking()
            .Where(w => w.BroadcasterId == broadcasterId && w.IsEnabled)
            .ToListAsync(cancellationToken);

        List<Widget> attached =
        [
            .. widgets
                .Where(w =>
                    w.EventSubscriptions.Contains(NowPlayingEventType)
                    || w.EventSubscriptions.Contains(PlayEventType)
                )
                .Where(w => presence.IsWidgetAttached(broadcasterId, w.Id)),
        ];
        if (attached.Count == 0)
            return [];

        Guid? reporting = store.ReportingWidget(broadcasterId);
        Widget owner =
            attached.FirstOrDefault(w => w.Id == reporting)
            ?? attached.OrderBy(w => w.CreatedAt).ThenBy(w => w.Id).First();
        return [owner.Id];
    }

    public async Task PlayAsync(
        Guid broadcasterId,
        IReadOnlyList<Guid> players,
        string videoId,
        CancellationToken cancellationToken = default
    )
    {
        YouTubePlayWidgetPayload payload = new(videoId, YouTubeMusicProvider.WatchUrl(videoId));
        foreach (Guid widgetId in players)
            await notifier.SendWidgetEventAsync(
                broadcasterId,
                widgetId,
                PlayEventType,
                payload,
                cancellationToken
            );
    }

    public Task PauseAsync(
        Guid broadcasterId,
        IReadOnlyList<Guid> players,
        CancellationToken cancellationToken = default
    ) => SendTransportAsync(broadcasterId, players, PauseEventType, cancellationToken);

    public Task ResumeAsync(
        Guid broadcasterId,
        IReadOnlyList<Guid> players,
        CancellationToken cancellationToken = default
    ) => SendTransportAsync(broadcasterId, players, ResumeEventType, cancellationToken);

    public async Task SeekAsync(
        Guid broadcasterId,
        IReadOnlyList<Guid> players,
        long positionMs,
        CancellationToken cancellationToken = default
    )
    {
        YouTubeSeekWidgetPayload payload = new(positionMs);
        foreach (Guid widgetId in players)
            await notifier.SendWidgetEventAsync(
                broadcasterId,
                widgetId,
                SeekEventType,
                payload,
                cancellationToken
            );
    }

    public async Task<bool> HandOverNextAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        YouTubeQueuedTrack? next = store.TakeNext(broadcasterId);
        if (next is null)
            return false;

        IReadOnlyList<Guid> open = await FindPlayersAsync(broadcasterId, cancellationToken);
        if (open.Count == 0)
        {
            store.SetNext(broadcasterId, next);
            return true;
        }

        await PlayAsync(broadcasterId, open, next.VideoId, cancellationToken);
        store.MarkPushed(broadcasterId, next);
        return true;
    }

    public async Task StopAsync(Guid broadcasterId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Guid> open = await FindPlayersAsync(broadcasterId, cancellationToken);
        await SendTransportAsync(broadcasterId, open, StopEventType, cancellationToken);
    }

    private async Task SendTransportAsync(
        Guid broadcasterId,
        IReadOnlyList<Guid> players,
        string eventType,
        CancellationToken cancellationToken
    )
    {
        YouTubeTransportWidgetPayload payload = new();
        foreach (Guid widgetId in players)
            await notifier.SendWidgetEventAsync(
                broadcasterId,
                widgetId,
                eventType,
                payload,
                cancellationToken
            );
    }
}
