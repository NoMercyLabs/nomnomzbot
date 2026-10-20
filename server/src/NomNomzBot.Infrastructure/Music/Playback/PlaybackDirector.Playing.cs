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

public sealed partial class PlaybackDirector
{
    private async Task OnIdleAsync(PlaybackInput input, CancellationToken ct)
    {
        if (input is RequestAdded && _queue.Head is not null && await FindOwnerMusicAsync(ct))
        {
            // The probe found owner music: the director follows it and the request waits for its end.
            if (State is { Kind: PlaybackStateKind.Playing, Item: null })
                await Dispatch(input, ct);

            return;
        }

        switch (input)
        {
            case RequestAdded or OutputAttached when _queue.Head is { } head:
                await BeginStartAsync(head, input.GetType().Name, "start_head", null, ct);
                break;
            case EngineReport report:
                await OnReportAsync(report, ct);
                break;
            default:
                await IgnoreAsync(input);
                break;
        }
    }

    /// <summary>
    /// A request arrived in Idle. Each engine that never reported is read once, so the owner's own music
    /// (started before the first poll) is found before the head starts over it. True when the director
    /// has left Idle because of what the probes found.
    /// </summary>
    private async Task<bool> FindOwnerMusicAsync(CancellationToken ct)
    {
        foreach (IPlaybackEngine engine in _engines.Values)
        {
            if (_reports.ContainsKey(engine.Kind) || !_probed.Add(engine.Kind))
                continue;

            EngineReport? found = await ProbeAsync(engine, ct);
            if (found is null)
                continue;

            _reports[engine.Kind] = found;
            if (found.IsPlaying)
                await OnReportAsync(found, ct);
        }

        return State.Kind != PlaybackStateKind.Idle;
    }

    /// <summary>A probe that throws is a probe with no answer. A cancel from the caller still propagates.</summary>
    private static async Task<EngineReport?> ProbeAsync(
        IPlaybackEngine engine,
        CancellationToken ct
    )
    {
        try
        {
            return await engine.ProbeAsync(ct);
        }
        catch (Exception ex)
            when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task OnPlayingAsync(PlaybackInput input, CancellationToken ct)
    {
        switch (input)
        {
            case EngineReport report:
                await OnReportAsync(report, ct);
                break;
            case TrackEnded ended when IsCurrentTrack(ended):
                await OnTrackEndedAsync(ended, ct);
                break;
            case TrackEnded ended when _youTubeEnding is { } finished && ended.Uri == finished.Uri:
                _youTubeEnding = null;
                await MoveAsync(State, nameof(TrackEnded), "youtube_finished", finished.Id);
                break;
            case TrackEnding ending
                when ending.Engine == State.Engine && ending.Uri == State.Item?.Uri:
                await OnTrackEndingAsync(ending, ct);
                break;
            case OwnerPause
            or OwnerSkip
            or OwnerPrevious
            or OwnerSeek
            or OwnerVolume
            or OwnerMute
            or DuckStart
            or DuckEnd:
                await OnOwnerIntentAsync(input, ct);
                break;
            case OutputAttached attached when State.Engine is { } engine:
                await ReattachOutputAsync(engine, attached, ct);
                break;
            case RequestAdded when State.Item is null:
                await MoveAsync(State, input, "waiting_for_track_end");
                break;
            default:
                await IgnoreAsync(input);
                break;
        }
    }

    /// <summary>
    /// An output page (re)attached while an engine holds the track: the engine gets the page state so it can
    /// put the current track back on the new page. The director state does not change.
    /// </summary>
    private async Task ReattachOutputAsync(
        EngineKind engine,
        OutputAttached attached,
        CancellationToken ct
    )
    {
        await CommandAsync(
            engine,
            nameof(OutputAttached),
            e => e.ReattachOutputAsync(attached.PageIsPlaying, ct)
        );
        await MoveAsync(State, attached, "output_reattached");
    }

    private async Task OnBlockedAsync(PlaybackInput input, CancellationToken ct)
    {
        // Music plays again (the owner started it, or the device came back): the director follows it, so a
        // waiting request lines up at that track's end.
        if (input is EngineReport { IsPlaying: true } playing)
        {
            await MoveAsync(PlaybackState.Idle, input, "music_playing");
            await OnReportAsync(playing, ct);
            return;
        }

        string? signal = null;
        if (_outputWait is { } wait)
        {
            signal = OutputWaitSignal(input, wait);
            if (signal is null)
            {
                // A poll tick on a waiting head is silent: no start, no journal entry.
                if (input is not (RequestAdded or EngineReport))
                    await IgnoreAsync(input);

                return;
            }

            EndOutputWait(wait, signal);
        }
        else if (input is not (OutputAttached or RequestAdded))
        {
            await IgnoreAsync(input);
            return;
        }

        // The poller's tick changes nothing while the head's engine still cannot start (no player page open).
        if (
            input is RequestAdded
            && _queue.Head is { } waiting
            && !await CanStartOnAsync(waiting.Engine, ct)
            && await StartableBehindAsync(waiting, ct) is null
        )
        {
            await IgnoreAsync(input);
            return;
        }

        await MoveAsync(
            PlaybackState.Idle,
            input,
            signal ?? (input is OutputAttached ? "output_attached" : "request_added")
        );
        if (await FollowPlayingMusicAsync(ct))
            return;

        if (_queue.Head is { } head)
            await BeginStartAsync(head, input.GetType().Name, "start_head", null, ct);
    }

    /// <summary>Music an engine last reported playing is followed, so the head never starts over it.</summary>
    private async Task<bool> FollowPlayingMusicAsync(CancellationToken ct)
    {
        if (_reports.Values.FirstOrDefault(r => r.IsPlaying) is not { } playing)
            return false;

        await OnReportAsync(playing, ct);

        return State.Kind != PlaybackStateKind.Idle;
    }

    private bool IsCurrentTrack(TrackEnded ended) =>
        ended.Engine == State.Engine && (State.Item is null || State.Item.Uri == ended.Uri);

    /// <summary>
    /// Row 28: a Spotify report during our own playing item names another device than the one that confirmed
    /// our start. The owner moved Spotify; the director blocks and sends no command (no automatic transfer).
    /// </summary>
    private bool MovedToAnotherDevice(EngineReport report) =>
        State is { Kind: PlaybackStateKind.Playing, Item: not null, Engine: EngineKind.Spotify }
        && report.Engine == EngineKind.Spotify
        && _confirmedDeviceId is not null
        && report.DeviceId is not null
        && report.DeviceId != _confirmedDeviceId;

    private async Task OnReportAsync(EngineReport report, CancellationToken ct)
    {
        if (MovedToAnotherDevice(report))
        {
            _confirmedDeviceId = null;
            await MoveAsync(
                PlaybackState.Blocked("Spotify moved to another device"),
                report,
                "device_changed"
            );
            return;
        }

        if (!report.IsPlaying)
        {
            if (State.Kind == PlaybackStateKind.Playing && report.Engine != State.Engine)
                await SilenceForeignAsync(report, ct);
            else
                await OnNotPlayingReportAsync(report);

            return;
        }

        // With no Music Player page open the video cannot take over, so the Spotify song plays on and its row
        // leaves the queue below.
        if (SpotifyMovedOnUnderYouTubeHead(report))
        {
            if (await CanStartOnAsync(EngineKind.YouTube, ct))
            {
                await TakeOverFromSpotifyAsync(nameof(EngineReport), ct);
                return;
            }

            await AnnouncePassedOverAsync(_queue.Head!, ct);
        }

        // A queued row that plays on its own leaves the queue first, even when another engine holds
        // the owner's music: the row was seen playing, so it is no longer waiting.
        if (FindRow(report) is { } row)
        {
            string reason = row.Id == _queue.Head?.Id ? "played_externally" : "played_out_of_order";
            await PlayRowAsync(row, report, reason);
            return;
        }

        if (State.Kind == PlaybackStateKind.Playing && report.Engine != State.Engine)
        {
            await SilenceForeignAsync(report, ct);
            return;
        }

        // The request already plays; the next Spotify request is lined up after every input (LineUpNextSpotifyAsync).
        if (State.Kind == PlaybackStateKind.Playing && State.Item?.Uri == report.Uri)
            return;

        bool ownerAlready = State is { Kind: PlaybackStateKind.Playing, Item: null };
        _ownerReport = report;
        if (!ownerAlready)
        {
            _memory = null;
            await MoveAsync(PlaybackState.Playing(report.Engine, null), report, "owner_music");
        }
    }

    private async Task OnTrackEndedAsync(TrackEnded ended, CancellationToken ct)
    {
        if (ended.Engine == EngineKind.Spotify && _queue.Head is { Engine: EngineKind.YouTube })
        {
            await TakeOverFromSpotifyAsync(nameof(TrackEnded), ct);
            return;
        }

        // A page that never sent ENDING (an old cached page): Spotify gets the music back at ENDED.
        if (
            ended.Engine == EngineKind.YouTube
            && _spotifyHeldForYouTube
            && _queue.Head is not { Engine: EngineKind.YouTube }
        )
        {
            await ReturnToSpotifyAsync(nameof(TrackEnded), ct);
            return;
        }

        if (_queue.Head is { } next)
        {
            await BeginStartAsync(next, nameof(TrackEnded), "next_item", null, ct);
            return;
        }

        await EndOfQueueAsync(ended.Engine, nameof(TrackEnded), ct);
    }
}
