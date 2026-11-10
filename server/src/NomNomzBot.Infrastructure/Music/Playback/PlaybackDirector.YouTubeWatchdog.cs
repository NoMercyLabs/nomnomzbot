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
/// A playing video must keep reporting. A video that goes silent, or whose page goes away, gives the
/// music back with no human action (Stoney, 2026-11-09: Spotify must not break while he streams).
/// </summary>
public sealed partial class PlaybackDirector
{
    private const string ReportTimeoutInput = "ReportTimeout";

    private static readonly TimeSpan ReportDeadline = TimeSpan.FromSeconds(15);

    /// <summary>How long a page that went away has to come back; a reload mid-video takes this long.</summary>
    private static readonly TimeSpan DetachGrace = TimeSpan.FromSeconds(5);

    /// <summary>When the watched video counts as lost; null when no video is watched.</summary>
    private DateTimeOffset? _reportDeadline;

    /// <summary>True when the page went away and nothing has proven the video alive since.</summary>
    private bool _outputDetached;

    /// <summary>A video is watched when a queue row owns it or Spotify waits for it; the owner's own is not.</summary>
    private void TrackReportDeadline(PlaybackState from, PlaybackState to)
    {
        bool watched =
            to is { Kind: PlaybackStateKind.Playing, Engine: EngineKind.YouTube }
            && (to.Item is not null || _spotifyHeldForYouTube);
        if (!watched)
        {
            _reportDeadline = null;
            _outputDetached = false;
        }
        else if (from == to)
        {
            _reportDeadline ??= _time.GetUtcNow() + ReportDeadline;
        }
        else
        {
            _reportDeadline = _time.GetUtcNow() + ReportDeadline;
            _outputDetached = false;
        }
    }

    /// <summary>Runs before the deadlines are raised, so a late report still proves the page alive.</summary>
    private void RenewReportDeadline(PlaybackInput input)
    {
        bool proofOfLife =
            input is OutputAttached
            || input is EngineSignal { Engine: EngineKind.YouTube } signal
                && (State.Item is null || signal.Uri == State.Item.Uri);
        if (_reportDeadline is null || !proofOfLife)
            return;

        _reportDeadline = _time.GetUtcNow() + ReportDeadline;
        _outputDetached = false;
    }

    private bool ReportDeadlinePassed() =>
        _reportDeadline is { } deadline && _time.GetUtcNow() > deadline;

    /// <summary>The last page left: the deadline shortens to the reload grace and never grows.</summary>
    private Task OnOutputDetachedAsync(OutputDetached input)
    {
        DateTimeOffset grace = _time.GetUtcNow() + DetachGrace;
        if (_reportDeadline > grace)
            _reportDeadline = grace;

        _outputDetached = true;

        return MoveAsync(State, input, "output_detached");
    }

    /// <summary>The video is lost: held Spotify comes back first, even with another video waiting.</summary>
    private async Task LoseVideoAsync(CancellationToken ct)
    {
        string reason = _outputDetached ? "youtube_output_lost" : "youtube_report_timeout";
        await StopBestEffortAsync(EngineKind.YouTube, ct);
        // Its last report still says playing; left in place it would pass for the owner's music.
        _reports.Remove(EngineKind.YouTube);
        await MoveAsync(State, ReportTimeoutInput, reason, State.Item?.Id);
        if (_spotifyHeldForYouTube)
        {
            await ReturnToSpotifyAsync(ReportTimeoutInput, ct);
            return;
        }

        if (_memory is not { WasPlaying: true } && _queue.Head is { } next)
        {
            await BeginStartAsync(LinedUpOr(next), ReportTimeoutInput, "next_item", null, ct);
            return;
        }

        await EndOfQueueAsync(EngineKind.YouTube, ReportTimeoutInput, ct);
    }

    /// <summary>The owner chose Spotify over the video the director holds it for: the hold ends here.</summary>
    private async Task GiveUpVideoForSpotifyAsync(PlaybackInput input, CancellationToken ct)
    {
        await StopBestEffortAsync(EngineKind.YouTube, ct);
        _reports.Remove(EngineKind.YouTube);
        await ReturnToSpotifyAsync(input.GetType().Name, ct);
    }

    /// <summary>
    /// Spotify plays while the video is past its deadline: the owner started it, so it is followed.
    /// Runs before the deadline is raised, which would send a Resume or a Skip to the playing Spotify.
    /// </summary>
    private async Task FollowSpotifyOverLostVideoAsync(PlaybackInput input, CancellationToken ct)
    {
        if (input is not EngineReport { Engine: EngineKind.Spotify, IsPlaying: true } report)
            return;

        if (!ReportDeadlinePassed())
            return;

        await StopBestEffortAsync(EngineKind.YouTube, ct);
        _reports.Remove(EngineKind.YouTube);
        ReleaseSpotifyHold();
        _ownerReport = report;
        await MoveAsync(
            PlaybackState.Playing(EngineKind.Spotify, null),
            nameof(EngineReport),
            "owner_music_over_lost_video"
        );
    }

    /// <summary>
    /// A stop that cannot reach the page must not hold the music. It skips CommandAsync on purpose: a
    /// missing page would move the director to Blocked and leave Spotify paused.
    /// </summary>
    private async Task StopBestEffortAsync(EngineKind kind, CancellationToken ct)
    {
        if (!_engines.TryGetValue(kind, out IPlaybackEngine? engine))
            return;

        try
        {
            await engine.StopAsync(ct);
        }
        catch (Exception ex)
            when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { }
    }
}
