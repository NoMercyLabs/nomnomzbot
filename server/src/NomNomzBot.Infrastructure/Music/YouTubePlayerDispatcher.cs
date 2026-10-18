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

namespace NomNomzBot.Infrastructure.Music;

/// <summary>Finds the channel's Audio page and tells it what to play.</summary>
public interface IYouTubePlayerDispatcher
{
    /// <summary>
    /// The enabled Audio widget on the selected Audio page, as a list of zero or one id.
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

    /// <summary>Sends <c>youtube.volume</c> with the percent (0-100) to the given player widgets.</summary>
    Task VolumeAsync(
        Guid broadcasterId,
        IReadOnlyList<Guid> players,
        int percent,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Hands the next queued request to the owning player, as an ended video does. With no player open the
    /// request stays queued for the next page that attaches; with nothing queued nothing is sent and the
    /// result is false.
    /// </summary>
    Task<bool> HandOverNextAsync(Guid broadcasterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts the current song back on an Audio page that just became ready: a reloaded page or a restarted OBS
    /// picks it up where it would be now. True when a song is on the page, so the next request keeps waiting.
    /// A page that already reported its video playing (it kept playing through a dropped connection) is left
    /// alone.
    /// </summary>
    Task<bool> ReplayCurrentAsync(
        Guid broadcasterId,
        bool pageIsPlaying,
        CancellationToken cancellationToken = default
    );

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
    public const string VolumeEventType = "youtube.volume";

    public async Task<IReadOnlyList<Guid>> FindPlayersAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        Guid? audioWidgetId = presence.GetAudioWidgetId(broadcasterId);
        if (audioWidgetId is null)
            return [];
        bool enabled = await db
            .Widgets.AsNoTracking()
            .AnyAsync(
                w => w.Id == audioWidgetId && w.BroadcasterId == broadcasterId && w.IsEnabled,
                cancellationToken
            );
        return enabled ? [audioWidgetId.Value] : [];
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

    public async Task VolumeAsync(
        Guid broadcasterId,
        IReadOnlyList<Guid> players,
        int percent,
        CancellationToken cancellationToken = default
    )
    {
        YouTubeVolumeWidgetPayload payload = new(percent);
        foreach (Guid widgetId in players)
            await notifier.SendWidgetEventAsync(
                broadcasterId,
                widgetId,
                VolumeEventType,
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

    public async Task<bool> ReplayCurrentAsync(
        Guid broadcasterId,
        bool pageIsPlaying,
        CancellationToken cancellationToken = default
    )
    {
        if (pageIsPlaying)
            return true;
        YouTubePlayerReport? current = store.ResumePoint(broadcasterId);
        if (current is null)
            return false;
        IReadOnlyList<Guid> open = await FindPlayersAsync(broadcasterId, cancellationToken);
        if (open.Count == 0)
            return false;

        await PlayAsync(broadcasterId, open, current.VideoId, cancellationToken);
        await SeekAsync(broadcasterId, open, current.PositionMs, cancellationToken);
        if (current.State == YouTubePlayerState.Paused)
            await PauseAsync(broadcasterId, open, cancellationToken);
        return true;
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
