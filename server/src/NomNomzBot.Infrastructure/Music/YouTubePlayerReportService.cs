// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Music;

public sealed class YouTubePlayerReportService(
    IYouTubePlayerStateStore store,
    IYouTubePlayerDispatcher players,
    IEventBus eventBus,
    IPlayOnceResumeTracker resumeTracker
) : IYouTubePlayerReportService
{
    public async Task<Result> ReportAsync(
        Guid broadcasterId,
        Guid widgetId,
        string videoId,
        string state,
        long positionMs,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(videoId))
            return Result.Failure("A YouTube player report needs a video id.", "INVALID_VIDEO_ID");
        if (!Enum.TryParse(state, ignoreCase: true, out YouTubePlayerState parsed))
            return Result.Failure($"Unknown YouTube player state '{state}'.", "INVALID_STATE");

        IReadOnlyList<Guid> owner = await players.FindPlayersAsync(
            broadcasterId,
            cancellationToken
        );
        if (
            owner.Count > 0
            && owner[0] != widgetId
            && store.GetFresh(broadcasterId)?.State == YouTubePlayerState.Playing
        )
            return Result.Failure(
                "Another widget owns this channel's YouTube player.",
                "NOT_PLAYER_OWNER"
            );

        // Looked up before an ended video hands over to the next one, which replaces the pushed track.
        YouTubeQueuedTrack? known = store.PushedTrack(broadcasterId, videoId);
        YouTubePlayerReport? previous = store.Report(
            broadcasterId,
            widgetId,
            videoId,
            parsed,
            positionMs
        );

        bool changed =
            previous is null
            || previous.State != parsed
            || !string.Equals(previous.VideoId, videoId, StringComparison.Ordinal);
        bool ended = parsed is YouTubePlayerState.Ended or YouTubePlayerState.Error;

        // A play-once video that ended is followed by the interrupted one, which the resume handler starts
        // off the event below; the waiting request plays after that one, so it is not handed over here.
        if (ended && !(changed && ResumePending(broadcasterId, videoId)))
            await players.HandOverNextAsync(broadcasterId, cancellationToken);

        if (changed)
            await PublishAsync(
                broadcasterId,
                videoId,
                parsed,
                positionMs,
                known,
                ended,
                cancellationToken
            );
        return Result.Success();
    }

    private bool ResumePending(Guid broadcasterId, string videoId) =>
        resumeTracker.TryPeek(broadcasterId, out PlayOnceResumeState pending)
        && pending.PriorTrackUri is not null
        && string.Equals(
            pending.InterruptingTrackUri,
            YouTubeMusicProvider.WatchUrl(videoId),
            StringComparison.Ordinal
        );

    private Task PublishAsync(
        Guid broadcasterId,
        string videoId,
        YouTubePlayerState state,
        long positionMs,
        YouTubeQueuedTrack? known,
        bool trackEnded,
        CancellationToken cancellationToken
    ) =>
        eventBus.PublishAsync(
            new PlaybackStateChangedEvent
            {
                BroadcasterId = broadcasterId,
                IsPlaying = state == YouTubePlayerState.Playing,
                TrackEnded = trackEnded,
                TrackName = known?.Title,
                Artist = known?.Artist,
                Provider = "youtube",
                TrackUri = YouTubeMusicProvider.WatchUrl(videoId),
                ProgressMs = (int)Math.Clamp(positionMs, 0, int.MaxValue),
                RepeatMode = MusicRepeatMode.Off,
                VolumePercent = 100,
                ObservedAt = DateTimeOffset.UtcNow,
            },
            cancellationToken
        );
}
