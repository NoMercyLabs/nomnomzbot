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

using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Music.Playback;

/// <summary>
/// The owner can turn YouTube off and Spotify is never held by it (Stoney, 2026-11-09). Waiting videos
/// leave the queue, a playing one stops, and a Spotify the director paused gets the music back.
/// </summary>
public sealed partial class PlaybackDirector
{
    private const string YouTubeOffInput = "YouTubeTurnedOff";

    /// <summary>With the one-second timer this acts within 20 seconds of the setting changing.</summary>
    private static readonly TimeSpan YouTubeOffCheckEvery = TimeSpan.FromSeconds(15);

    private DateTimeOffset _nextYouTubeOffCheck = DateTimeOffset.MinValue;

    /// <summary>The setting is read only while a video could be affected, so Spotify alone costs nothing.</summary>
    private bool YouTubeIsInvolved() =>
        State.Engine == EngineKind.YouTube
        || _spotifyHeldForYouTube
        || _outputWait is { Engine: EngineKind.YouTube }
        || _queue.Snapshot.Any(row => row.Engine == EngineKind.YouTube);

    /// <summary>Runs from the deadline timer only, so a standby instance never removes a row.</summary>
    private async Task ApplyYouTubeOffSwitchAsync(CancellationToken ct)
    {
        DateTimeOffset now = _time.GetUtcNow();
        if (_youTubeOff is null || now < _nextYouTubeOffCheck || !YouTubeIsInvolved())
            return;

        _nextYouTubeOffCheck = now + YouTubeOffCheckEvery;
        if (await _youTubeOff.IsOffAsync(_broadcasterId, ct))
            await TurnYouTubeOffAsync(ct);
    }

    private async Task TurnYouTubeOffAsync(CancellationToken ct)
    {
        await RemoveWaitingVideosAsync();
        if (State.Engine == EngineKind.YouTube)
        {
            await StopVideoForOffSwitchAsync(ct);
            return;
        }

        if (_spotifyHeldForYouTube && State.Engine is null)
        {
            await ReturnToSpotifyAsync(YouTubeOffInput, ct);
            return;
        }

        if (_outputWait is { Engine: EngineKind.YouTube })
            await EndWaitForVideoPageAsync(ct);
    }

    private async Task RemoveWaitingVideosAsync()
    {
        foreach (PlaybackItem row in _queue.Snapshot.Where(r => r.Engine == EngineKind.YouTube))
        {
            _queue.Remove(row.Id);
            _failedStarts.Remove(row.Id);
            _queuedAhead.Remove(row.Id);
            _passedOver.Remove(row.Id);
            await MoveAsync(State, YouTubeOffInput, "youtube_row_removed", row.Id);
        }
    }

    private async Task StopVideoForOffSwitchAsync(CancellationToken ct)
    {
        await StopBestEffortAsync(EngineKind.YouTube, ct);
        // Its last report still says playing; left in place it would pass for the owner's music.
        _reports.Remove(EngineKind.YouTube);
        await MoveAsync(State, YouTubeOffInput, "youtube_turned_off", State.Item?.Id);
        await GiveMusicOnAfterVideoAsync(YouTubeOffInput, ct);
    }

    /// <summary>Only videos waited for the page, so what is left can run.</summary>
    private async Task EndWaitForVideoPageAsync(CancellationToken ct)
    {
        await MoveAsync(PlaybackState.Idle, YouTubeOffInput, "youtube_turned_off");
        if (await FollowPlayingMusicAsync(ct))
            return;

        if (_queue.Head is { } head)
            await BeginStartAsync(head, YouTubeOffInput, "start_head", null, ct);
    }
}
