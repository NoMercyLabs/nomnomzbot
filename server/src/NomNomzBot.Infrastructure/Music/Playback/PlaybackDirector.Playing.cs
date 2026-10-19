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
            // The probe found owner music; a hand-over near its end already started the head.
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
                if (!await HandOverIfNearEndAsync(nameof(RequestAdded), ct))
                    await MoveAsync(State, input, "waiting_near_end");
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
        if (input is not (OutputAttached or RequestAdded))
        {
            await IgnoreAsync(input);
            return;
        }

        await MoveAsync(
            PlaybackState.Idle,
            input,
            input is OutputAttached ? "output_attached" : "request_added"
        );
        if (_queue.Head is { } head)
            await BeginStartAsync(head, input.GetType().Name, "start_head", null, ct);
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

        if (State.Kind == PlaybackStateKind.Playing && State.Item?.Uri == report.Uri)
            return;

        bool ownerAlready = State is { Kind: PlaybackStateKind.Playing, Item: null };
        _ownerReport = report;
        if (!ownerAlready)
        {
            _memory = null;
            await MoveAsync(PlaybackState.Playing(report.Engine, null), report, "owner_music");
        }

        await HandOverIfNearEndAsync(nameof(EngineReport), ct);
    }

    /// <summary>Owner music is in its last seconds and a request waits: the head takes over.</summary>
    private async Task<bool> HandOverIfNearEndAsync(string inputName, CancellationToken ct)
    {
        if (
            _queue.Head is not { } head
            || _ownerReport is not { DurationMs: > 0 } owner
            || owner.DurationMs - owner.ProgressMs > NearEndMs
        )
            return false;

        bool otherEngine = head.Engine != owner.Engine;
        if (otherEngine)
            _memory = new(owner.Engine, owner.IsPlaying);

        await BeginStartAsync(head, inputName, "hand_over", otherEngine ? owner.Engine : null, ct);
        return true;
    }

    private async Task OnTrackEndedAsync(TrackEnded ended, CancellationToken ct)
    {
        if (_queue.Head is { } next)
        {
            await BeginStartAsync(next, nameof(TrackEnded), "next_item", null, ct);
            return;
        }

        await EndOfQueueAsync(ended.Engine, nameof(TrackEnded), ct);
    }
}
