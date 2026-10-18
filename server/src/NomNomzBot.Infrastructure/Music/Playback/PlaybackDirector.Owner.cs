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
    private async Task OnOwnerIntentAsync(PlaybackInput input, CancellationToken ct)
    {
        EngineKind engine = State.Engine!.Value;
        switch (input)
        {
            case OwnerPause:
                await _engines[engine].PauseAsync(ct);
                MarkSent(engine);
                await MoveAsync(
                    PlaybackState.PausedByOwner(engine, State.Item),
                    input,
                    "owner_pause"
                );
                break;
            case OwnerSkip:
                await OnSkipAsync(engine, input, ct);
                break;
            case OwnerPrevious:
                await OnPreviousAsync(engine, input, ct);
                break;
            case OwnerSeek seek:
                await _engines[engine].SeekAsync(seek.PositionMs, ct);
                await MoveAsync(State, input, "seek");
                break;
            case OwnerVolume volume:
                _volume = Math.Clamp(volume.Level, 0, 100);
                if (!_muted)
                    await _engines[engine].SetVolumeAsync(_volume, ct);
                await MoveAsync(State, input, "volume");
                break;
            case OwnerMute mute:
                _muted = mute.On;
                await _engines[engine].SetVolumeAsync(_muted ? 0 : _volume, ct);
                await MoveAsync(State, input, _muted ? "mute" : "unmute");
                break;
        }
    }

    private async Task OnSkipAsync(EngineKind engine, PlaybackInput input, CancellationToken ct)
    {
        string name = input.GetType().Name;
        if (_queue.Head is { } head)
        {
            if (State.Item is null && head.Engine != engine)
                _memory = new(engine, true);

            await BeginStartAsync(head, name, "owner_skip", engine, ct);
            return;
        }

        if (State.Item is null)
        {
            await IgnoreAsync(input);
            return;
        }

        await _engines[engine].StopAsync(ct);
        MarkSent(engine);
        await EndOfQueueAsync(engine, name, ct);
    }

    private async Task OnPreviousAsync(EngineKind engine, PlaybackInput input, CancellationToken ct)
    {
        PlaybackItem? current = State.Item;
        if (CurrentProgressMs() > RestartThresholdMs)
        {
            await RestartAsync(engine, input, ct);
            return;
        }

        if (current is not null && _history.Last?.Id == current.Id)
            _history.Pop();

        if (_history.Pop() is not { } previous)
        {
            if (current is not null)
                _history.Push(current);
            await RestartAsync(engine, input, ct);
            return;
        }

        if (current is not null)
            _queue.RequeueAtHead(current);
        else if (previous.Engine != engine)
            _memory = new(engine, true);

        _queue.RequeueAtHead(previous);
        await BeginStartAsync(previous, input.GetType().Name, "previous_item", engine, ct);
    }

    private async Task RestartAsync(EngineKind engine, PlaybackInput input, CancellationToken ct)
    {
        await _engines[engine].SeekAsync(0, ct);
        await MoveAsync(State, input, "previous_restart");
    }

    /// <summary>Progress of the track the state engine plays, from its latest report; 0 when unknown.</summary>
    private int CurrentProgressMs() =>
        State.Engine is { } engine
        && _reports.TryGetValue(engine, out EngineReport? report)
        && (State.Item is null || report.Uri == State.Item.Uri)
            ? report.ProgressMs
            : 0;

    /// <summary>No queue item follows: resume the owner's remembered music, or go Idle.</summary>
    private async Task EndOfQueueAsync(EngineKind ended, string inputName, CancellationToken ct)
    {
        OwnerMusicMemory? memory = _memory;
        _memory = null;
        if (memory is { WasPlaying: true } && memory.Engine != ended)
        {
            await _engines[memory.Engine].ResumeAsync(ct);
            MarkSent(memory.Engine);
            _ownerReport = null;
            await MoveAsync(
                PlaybackState.Playing(memory.Engine, null),
                inputName,
                "owner_music_resumed"
            );
            return;
        }

        await MoveAsync(PlaybackState.Idle, inputName, "queue_empty");
    }

    private void MarkSent(EngineKind engine) => _lastSent[engine] = _time.GetUtcNow();

    /// <summary>True when we sent this engine a command so recently that its report may be the echo.</summary>
    private bool SentRecently(EngineKind engine) =>
        _lastSent.TryGetValue(engine, out DateTimeOffset at) && _time.GetUtcNow() - at < OwnerEcho;

    /// <summary>An engine that is not the state's engine plays: pause it and journal it.</summary>
    private async Task SilenceForeignAsync(EngineReport report, CancellationToken ct)
    {
        if (
            !report.IsPlaying
            || SentRecently(report.Engine)
            || !_engines.TryGetValue(report.Engine, out IPlaybackEngine? engine)
        )
            return;

        await engine.PauseAsync(ct);
        MarkSent(report.Engine);
        await MoveAsync(State, report, "foreign_engine_silenced");
    }

    /// <summary>A paused report from the playing engine: the owner paused it unless we did or it is ending.</summary>
    private async Task OnNotPlayingReportAsync(EngineReport report)
    {
        if (State.Kind != PlaybackStateKind.Playing)
            return;

        string? expectedUri = State.Item?.Uri ?? _ownerReport?.Uri;
        bool midTrack = report.DurationMs > 0 && report.DurationMs - report.ProgressMs > NearEndMs;
        bool ownerPaused = report.Uri == expectedUri && midTrack && !SentRecently(report.Engine);
        if (State.Item is null)
            _ownerReport = report;

        if (ownerPaused)
            await MoveAsync(
                PlaybackState.PausedByOwner(report.Engine, State.Item),
                report,
                "owner_pause_detected"
            );
    }
}
