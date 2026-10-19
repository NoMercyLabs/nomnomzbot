// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// A video handed to the player, with what the provider resolved for it: title and channel (empty when
/// unknown), length (0 when unknown) and thumbnail.
/// </summary>
public sealed record YouTubeQueuedTrack(
    string VideoId,
    string Title,
    string Artist,
    int DurationMs = 0,
    string? ThumbnailUrl = null
);

/// <summary>
/// What each channel's overlay YouTube player last said, the video pushed to it, and the one video waiting
/// behind it. A report older than <see cref="YouTubePlayerStateStore.Staleness"/> counts as no report: a
/// closed page stops reporting, and its player is then idle.
/// </summary>
public interface IYouTubePlayerStateStore
{
    /// <summary>Records the report and returns the fresh report it replaces, or null.</summary>
    YouTubePlayerReport? Report(
        Guid broadcasterId,
        Guid widgetId,
        string videoId,
        YouTubePlayerState state,
        long positionMs
    );

    YouTubePlayerReport? GetFresh(Guid broadcasterId);

    /// <summary>
    /// Where a newly opened Audio page picks the song up: the last playing or paused report within
    /// <see cref="YouTubePlayerStateStore.ResumeWindow"/>, a playing one moved on by the time since, else null.
    /// </summary>
    YouTubePlayerReport? ResumePoint(Guid broadcasterId);

    /// <summary>The widget whose report the store accepted last, or null when none has reported.</summary>
    Guid? ReportingWidget(Guid broadcasterId);

    /// <summary>True while a video plays or is paused, or was pushed and the player has not answered yet.</summary>
    bool IsBusy(Guid broadcasterId);

    void MarkPushed(Guid broadcasterId, YouTubeQueuedTrack track);

    /// <summary>The last pushed track when its video id is <paramref name="videoId"/>, else null.</summary>
    YouTubeQueuedTrack? PushedTrack(Guid broadcasterId, string videoId);

    /// <summary>True once a video has been pushed to the channel's player.</summary>
    bool HasPushed(Guid broadcasterId);

    /// <summary>The pushed track while the player has not answered for it yet and the push is not stale, else null.</summary>
    YouTubeQueuedTrack? PendingPushed(Guid broadcasterId);

    void SetNext(Guid broadcasterId, YouTubeQueuedTrack track);

    YouTubeQueuedTrack? TakeNext(Guid broadcasterId);

    YouTubeQueuedTrack? PeekNext(Guid broadcasterId);
}

public sealed class YouTubePlayerStateStore : IYouTubePlayerStateStore
{
    public static readonly TimeSpan Staleness = TimeSpan.FromSeconds(15);

    /// <summary>A song reported this long ago belongs to an earlier stream and is not picked up again.</summary>
    public static readonly TimeSpan ResumeWindow = TimeSpan.FromHours(4);

    private sealed class ChannelPlayer
    {
        public YouTubePlayerReport? Last;
        public Guid? ReportingWidget;
        public YouTubeQueuedTrack? Pushed;
        public bool AwaitingAnswer;
        public DateTimeOffset PushedAt;
        public YouTubeQueuedTrack? Next;
    }

    private readonly ConcurrentDictionary<Guid, ChannelPlayer> _players = new();
    private readonly TimeProvider _clock;

    public YouTubePlayerStateStore(TimeProvider clock)
    {
        _clock = clock;
    }

    public YouTubePlayerReport? Report(
        Guid broadcasterId,
        Guid widgetId,
        string videoId,
        YouTubePlayerState state,
        long positionMs
    )
    {
        ChannelPlayer player = PlayerOf(broadcasterId);
        lock (player)
        {
            YouTubePlayerReport? previous = FreshOrNull(player.Last);
            player.Last = new(videoId, state, positionMs, _clock.GetUtcNow());
            player.ReportingWidget = widgetId;
            if (string.Equals(player.Pushed?.VideoId, videoId, StringComparison.Ordinal))
                player.AwaitingAnswer = false;
            return previous;
        }
    }

    public YouTubePlayerReport? GetFresh(Guid broadcasterId)
    {
        ChannelPlayer player = PlayerOf(broadcasterId);
        lock (player)
            return FreshOrNull(player.Last);
    }

    public YouTubePlayerReport? ResumePoint(Guid broadcasterId)
    {
        ChannelPlayer player = PlayerOf(broadcasterId);
        lock (player)
        {
            YouTubePlayerReport? last = player.Last;
            if (last?.State is not (YouTubePlayerState.Playing or YouTubePlayerState.Paused))
                return null;
            TimeSpan since = _clock.GetUtcNow() - last.ReportedAt;
            if (since > ResumeWindow)
                return null;
            // The song kept time while no page played it; past its end the player reports ENDED by itself.
            return last.State == YouTubePlayerState.Playing
                ? last with
                {
                    PositionMs = last.PositionMs + (long)since.TotalMilliseconds,
                }
                : last;
        }
    }

    public Guid? ReportingWidget(Guid broadcasterId)
    {
        ChannelPlayer player = PlayerOf(broadcasterId);
        lock (player)
            return player.ReportingWidget;
    }

    public bool IsBusy(Guid broadcasterId)
    {
        ChannelPlayer player = PlayerOf(broadcasterId);
        lock (player)
        {
            bool awaitingAnswer =
                player.AwaitingAnswer && _clock.GetUtcNow() - player.PushedAt < Staleness;
            return awaitingAnswer
                || FreshOrNull(player.Last)?.State
                    is YouTubePlayerState.Playing
                        or YouTubePlayerState.Paused;
        }
    }

    public void MarkPushed(Guid broadcasterId, YouTubeQueuedTrack track)
    {
        ChannelPlayer player = PlayerOf(broadcasterId);
        lock (player)
        {
            player.Pushed = track;
            player.AwaitingAnswer = true;
            player.PushedAt = _clock.GetUtcNow();
        }
    }

    public YouTubeQueuedTrack? PushedTrack(Guid broadcasterId, string videoId)
    {
        ChannelPlayer player = PlayerOf(broadcasterId);
        lock (player)
            return string.Equals(player.Pushed?.VideoId, videoId, StringComparison.Ordinal)
                ? player.Pushed
                : null;
    }

    public bool HasPushed(Guid broadcasterId)
    {
        ChannelPlayer player = PlayerOf(broadcasterId);
        lock (player)
            return player.Pushed is not null;
    }

    public YouTubeQueuedTrack? PendingPushed(Guid broadcasterId)
    {
        ChannelPlayer player = PlayerOf(broadcasterId);
        lock (player)
            return player.AwaitingAnswer && _clock.GetUtcNow() - player.PushedAt < Staleness
                ? player.Pushed
                : null;
    }

    public void SetNext(Guid broadcasterId, YouTubeQueuedTrack track)
    {
        ChannelPlayer player = PlayerOf(broadcasterId);
        lock (player)
            player.Next = track;
    }

    public YouTubeQueuedTrack? TakeNext(Guid broadcasterId)
    {
        ChannelPlayer player = PlayerOf(broadcasterId);
        lock (player)
        {
            YouTubeQueuedTrack? next = player.Next;
            player.Next = null;
            return next;
        }
    }

    public YouTubeQueuedTrack? PeekNext(Guid broadcasterId)
    {
        ChannelPlayer player = PlayerOf(broadcasterId);
        lock (player)
            return player.Next;
    }

    private ChannelPlayer PlayerOf(Guid broadcasterId) =>
        _players.GetOrAdd(broadcasterId, _ => new ChannelPlayer());

    private YouTubePlayerReport? FreshOrNull(YouTubePlayerReport? report) =>
        report is not null && _clock.GetUtcNow() - report.ReportedAt < Staleness ? report : null;
}
