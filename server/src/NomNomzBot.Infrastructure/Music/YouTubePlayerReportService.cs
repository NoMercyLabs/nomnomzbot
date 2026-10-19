// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Music;

public sealed class YouTubePlayerReportService(
    IYouTubePlayerStateStore store,
    IYouTubePlayerDispatcher players,
    IEventBus eventBus,
    IEngineFeed engineFeed,
    IPlayOnceResumeTracker resumeTracker,
    ILogger<YouTubePlayerReportService> logger
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
        if (!TryParseState(state, out YouTubePlayerState parsed))
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
        // A report about a video the bot has already replaced (a retried ENDED, a heartbeat in flight while the
        // next video was pushed) describes nothing current; acting on it would hand over the song after next.
        if (known is null && store.HasPushed(broadcasterId))
            return Result.Failure(
                "The report is about a video the player has already moved past.",
                "STALE_VIDEO"
            );
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

        // Every accepted report reaches the engine, a heartbeat of the same state included, so its progress
        // stays current. An ended video makes the director start the next request; nothing here pushes it.
        // A play-once video that ended is no director item, so it advances nothing.
        // A play-once video that ended with a resume pending is the exception: the resume handler restarts the
        // interrupted video off the event below, so the director must not also start the waiting request.
        bool fed =
            !(ended && changed && ResumePending(broadcasterId, videoId))
            && await FeedAsync(broadcasterId, videoId, parsed, positionMs, cancellationToken);

        // Announced before the playback change below, whose queue reconciler drops the failed request.
        // With no director to take the report, this is the only place the failure is announced.
        if (changed && parsed == YouTubePlayerState.Error && !fed)
            await eventBus.PublishAsync(
                new SongPlaybackFailedEvent
                {
                    BroadcasterId = broadcasterId,
                    TrackUri = YouTubeMusicProvider.WatchUrl(videoId),
                    TrackName = known?.Title ?? string.Empty,
                },
                cancellationToken
            );
        if (changed)
            await PublishAsync(
                broadcasterId,
                videoId,
                parsed,
                positionMs,
                known,
                ended,
                fed,
                cancellationToken
            );
        return Result.Success();
    }

    /// <summary>A feed failure never fails the report: the player's state is already stored.</summary>
    private async Task<bool> FeedAsync(
        Guid broadcasterId,
        string videoId,
        YouTubePlayerState state,
        long positionMs,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await engineFeed.ReportPlayerAsync(
                broadcasterId,
                new(videoId, state, positionMs, DateTimeOffset.UtcNow),
                cancellationToken
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "The YouTube player report for channel {BroadcasterId} did not reach the playback engine.",
                broadcasterId
            );

            return false;
        }
    }

    private bool ResumePending(Guid broadcasterId, string videoId) =>
        resumeTracker.TryPeek(broadcasterId, out PlayOnceResumeState pending)
        && pending.PriorTrackUri is not null
        && string.Equals(
            pending.InterruptingTrackUri,
            YouTubeMusicProvider.WatchUrl(videoId),
            StringComparison.Ordinal
        );

    // Only the four names: Enum.TryParse alone also takes a number ("7") or a comma list ("Playing, Paused").
    private static bool TryParseState(string state, out YouTubePlayerState parsed) =>
        Enum.TryParse(state, ignoreCase: true, out parsed)
        && Enum.GetNames<YouTubePlayerState>()
            .Contains(state.Trim(), StringComparer.OrdinalIgnoreCase);

    private Task PublishAsync(
        Guid broadcasterId,
        string videoId,
        YouTubePlayerState state,
        long positionMs,
        YouTubeQueuedTrack? known,
        bool trackEnded,
        bool fedToEngine,
        CancellationToken cancellationToken
    ) =>
        eventBus.PublishAsync(
            new PlaybackStateChangedEvent
            {
                BroadcasterId = broadcasterId,
                IsPlaying = state == YouTubePlayerState.Playing,
                TrackEnded = trackEnded,
                FedToEngine = fedToEngine,
                TrackName = known?.Title,
                Artist = known?.Artist,
                DurationMs = known?.DurationMs ?? 0,
                AlbumArtUrl = known?.ThumbnailUrl,
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
