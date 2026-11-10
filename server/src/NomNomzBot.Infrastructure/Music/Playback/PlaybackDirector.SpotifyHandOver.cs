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

    /// <summary>The song the fair head just replaced; a poll that still names it is not a play.</summary>
    private string? _displacedUri;

    /// <summary>True while Spotify is paused by the director for a YouTube run; it resumes at the run's end.</summary>
    private bool _spotifyHeldForYouTube;

    /// <summary>The item whose one renewal while Spotify is paused is used; null when none.</summary>
    private string? _renewedWhilePausedId;

    /// <summary>The video whose last seconds still play after Spotify got the music back; never silenced.</summary>
    private PlaybackItem? _youTubeEnding;

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

    /// <summary>
    /// A Spotify start in progress is a queue add plus a skip: a request lined up before it would be the track
    /// that skip plays, so lining up waits until that start is confirmed.
    /// </summary>
    private bool SpotifyCanTakeALineUp() =>
        State is not { Kind: PlaybackStateKind.Starting, Engine: EngineKind.Spotify }
        && (
            _spotifyHeldForYouTube
            || _reports.TryGetValue(EngineKind.Spotify, out EngineReport? report)
                && report.IsPlaying
        );

    /// <summary>The lined-up request while it still waits in the queue.</summary>
    private PlaybackItem? LinedUpRow() =>
        _linedUpId is null ? null : _queue.Snapshot.FirstOrDefault(i => i.Id == _linedUpId);

    /// <summary>
    /// What Spotify plays next. A fair-queue insert can rank a new request above the row already in Spotify's own
    /// queue, but Spotify still plays that row first and a start is a queue add plus a skip, so starting the new
    /// head would skip the lined-up song (live 2026-11-08).
    /// </summary>
    private PlaybackItem LinedUpOr(PlaybackItem head) =>
        head.Engine == EngineKind.Spotify && LinedUpRow() is { } linedUp ? linedUp : head;

    /// <summary>
    /// The fair order wins (Stoney, 2026-11-08). Spotify reached the lined-up song while a request ranked above
    /// it: the head starts now, and the lined-up song stays queued in its fair place, uncounted, to be lined up
    /// again. Spotify's own queue cannot be reordered, so this can only happen once Spotify has reached the song.
    /// </summary>
    private async Task<bool> StartsFairHeadInsteadAsync(EngineReport report, CancellationToken ct)
    {
        if (
            report is not { Engine: EngineKind.Spotify, IsPlaying: true }
            || LinedUpRow() is not { } linedUp
            || linedUp.Uri != report.Uri
            || _queue.Head is not { Engine: EngineKind.Spotify } head
            || head.Id == linedUp.Id
        )
            return false;

        _linedUpId = null;
        _queuedAhead.Remove(linedUp.Id);
        _displacedUri = linedUp.Uri;
        await BeginStartAsync(head, nameof(EngineReport), "fair_order_first", null, ct);

        return true;
    }

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
        if (
            !await BeginStartHoldingSpotifyAsync(
                head,
                inputName,
                "youtube_takeover",
                EngineKind.Spotify,
                ct
            )
        )
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

        PlaybackItem? video = State.Item;
        if (await ReturnToSpotifyAsync(nameof(TrackEnding), ct))
            _youTubeEnding = video;
    }

    /// <summary>
    /// Resumes the Spotify song the takeover paused. A lined-up request that arrived after that pause is not
    /// the paused song, so Spotify skips to it instead.
    /// </summary>
    private async Task<bool> ReturnToSpotifyAsync(string inputName, CancellationToken ct)
    {
        ReleaseSpotifyHold();
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
        // After a skip Spotify plays a track no report has named yet; the next poll names it.
        _ownerReport = skip || paused is null ? null : paused with { IsPlaying = true };
        await MoveAsync(
            PlaybackState.Playing(EngineKind.Spotify, null),
            inputName,
            skip ? "spotify_skipped_to_request" : "spotify_resumed"
        );
        return true;
    }

    private void ReleaseSpotifyHold()
    {
        _spotifyHeldForYouTube = false;
        _memory = null;
        _pausedForHandOver = null;
    }

    /// <summary>The item waits in the queue of a Spotify the director paused: a start alone would not play it.</summary>
    private bool WaitsInHeldSpotify(PlaybackItem item) =>
        _spotifyHeldForYouTube
        && item.Engine == EngineKind.Spotify
        && _queuedAhead.Contains(item.Id);

    /// <summary>
    /// Ends the hold for a song Spotify already has: Resume when the paused track is that song, else Skip
    /// to it. False when the command was blocked; the state then shows the block.
    /// </summary>
    private async Task<bool> SendHeldSpotifyToAsync(
        PlaybackItem item,
        string inputName,
        CancellationToken ct
    )
    {
        ReleaseSpotifyHold();
        bool skip = _reports.GetValueOrDefault(EngineKind.Spotify)?.Uri != item.Uri;
        if (
            !await CommandAsync(
                EngineKind.Spotify,
                inputName,
                e => skip ? e.SkipAsync(ct) : e.ResumeAsync(ct)
            )
        )
            return false;

        MarkSent(EngineKind.Spotify);
        return true;
    }

    /// <summary>
    /// A song in Spotify's queue keeps waiting; the owner's own pause is never undone. A Spotify the director
    /// holds for a video never reaches it, so that gets one renewal, then a Resume: the song stays queued.
    /// </summary>
    private async Task KeepWaitingOrResumeQueueAsync(
        PlaybackItem item,
        string inputName,
        CancellationToken ct
    )
    {
        EngineReport? last = _reports.GetValueOrDefault(item.Engine);
        if (!_spotifyHeldForYouTube || _renewedWhilePausedId != item.Id)
        {
            _renewedWhilePausedId = _spotifyHeldForYouTube ? item.Id : null;
            _startDeadline = _time.GetUtcNow() + StartDeadline;
            await MoveAsync(State, inputName, "start_still_queued", item.Id);
            return;
        }

        _renewedWhilePausedId = null;
        ReleaseSpotifyHold();
        if (!await CommandAsync(item.Engine, inputName, e => e.ResumeAsync(ct)))
            return;

        MarkSent(item.Engine);
        _ownerReport = last is null ? null : last with { IsPlaying = true };
        await MoveAsync(
            PlaybackState.Playing(item.Engine, null),
            inputName,
            "spotify_resumed_queue",
            item.Id
        );
    }
}
