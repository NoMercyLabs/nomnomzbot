// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Music.Playback;

/// <summary>
/// Stoney, 2026-10-19 night: Spotify keeps its own queue plus ONE lined-up request; YouTube takes over when
/// Spotify's next song starts and gives Spotify back 3 seconds before the video ends
/// (music-playback-determinism.md, "Changed 2026-10-19 night").
/// </summary>
public sealed partial class PlaybackDirector
{
    /// <summary>The one request lined up in Spotify's own queue and not yet seen playing; null when none.</summary>
    private string? _linedUpId;

    /// <summary>True while Spotify is paused by the director for a YouTube run; it resumes at the run's end.</summary>
    private bool _spotifyHeldForYouTube;

    /// <summary>The video whose last seconds still play after Spotify got the music back; never silenced.</summary>
    private string? _youTubeEndingUri;

    /// <summary>
    /// The first Spotify request in fair order goes into Spotify's own queue, one at a time, while Spotify plays
    /// or is held for YouTube. A refused add is tried again on a later input.
    /// </summary>
    private async Task LineUpNextSpotifyAsync(string inputName, CancellationToken ct)
    {
        if (
            !_engines.TryGetValue(EngineKind.Spotify, out IPlaybackEngine? spotify)
            || !SpotifyCanTakeALineUp()
            || LinedUpRow() is not null
        )
            return;

        PlaybackItem? next = _queue.Snapshot.FirstOrDefault(i =>
            i.Engine == EngineKind.Spotify && !_queuedAhead.Contains(i.Id)
        );
        if (next is null)
            return;

        EngineStartResult result = await TryStartAsync(
            spotify,
            next,
            e => e.QueueNextAsync(next, ct),
            ct
        );
        if (!result.Accepted)
            return;

        _queuedAhead.Add(next.Id);
        _linedUpId = next.Id;
        await MoveAsync(State, inputName, "lined_up", next.Id);
    }

    private bool SpotifyCanTakeALineUp() =>
        _spotifyHeldForYouTube
        || _reports.TryGetValue(EngineKind.Spotify, out EngineReport? report) && report.IsPlaying;

    /// <summary>The lined-up request while it still waits in the queue.</summary>
    private PlaybackItem? LinedUpRow() =>
        _linedUpId is null ? null : _queue.Snapshot.FirstOrDefault(i => i.Id == _linedUpId);

    /// <summary>Spotify plays and its next track just started while a YouTube request is the head.</summary>
    private bool SpotifyMovedOnUnderYouTubeHead(EngineReport report) =>
        report is { Engine: EngineKind.Spotify, IsPlaying: true }
        && State is { Kind: PlaybackStateKind.Playing, Engine: EngineKind.Spotify }
        && _queue.Head is { Engine: EngineKind.YouTube }
        && (State.Item?.Uri ?? _ownerReport?.Uri) is { } current
        && report.Uri != current;

    /// <summary>
    /// Spotify's next song starts: pause Spotify, put that song back to its start, and play the YouTube head.
    /// A lined-up request Spotify just reached stays in the queue: it plays when Spotify resumes.
    /// </summary>
    private async Task TakeOverFromSpotifyAsync(string inputName, CancellationToken ct)
    {
        PlaybackItem head = _queue.Head!;
        _memory = new(EngineKind.Spotify, true);
        await BeginStartAsync(head, inputName, "youtube_takeover", EngineKind.Spotify, ct);
        if (_pausedForHandOver != EngineKind.Spotify)
            return;

        _spotifyHeldForYouTube = true;
        await CommandAsync(EngineKind.Spotify, inputName, e => e.SeekAsync(0, ct));
    }

    /// <summary>The playing video has 3 seconds left: Spotify gets the music back unless another video follows.</summary>
    private async Task OnTrackEndingAsync(TrackEnding ending, CancellationToken ct)
    {
        if (!_spotifyHeldForYouTube || _queue.Head is { Engine: EngineKind.YouTube })
        {
            await IgnoreAsync(ending);
            return;
        }

        if (await ReturnToSpotifyAsync(nameof(TrackEnding), ct))
            _youTubeEndingUri = ending.Uri;
    }

    /// <summary>
    /// Resumes the Spotify song the takeover paused. A lined-up request that arrived after that pause is not
    /// the paused song, so Spotify skips to it instead.
    /// </summary>
    private async Task<bool> ReturnToSpotifyAsync(string inputName, CancellationToken ct)
    {
        _spotifyHeldForYouTube = false;
        _memory = null;
        _pausedForHandOver = null;
        EngineReport? paused = _reports.GetValueOrDefault(EngineKind.Spotify);
        bool skip = LinedUpRow() is { } waiting && paused?.Uri != waiting.Uri;
        if (
            !await CommandAsync(
                EngineKind.Spotify,
                inputName,
                e => skip ? e.SkipAsync(ct) : e.ResumeAsync(ct)
            )
        )
            return false;

        MarkSent(EngineKind.Spotify);
        _ownerReport = paused is null ? null : paused with { IsPlaying = true };
        await MoveAsync(
            PlaybackState.Playing(EngineKind.Spotify, null),
            inputName,
            skip ? "spotify_skipped_to_request" : "spotify_resumed"
        );
        return true;
    }
}
