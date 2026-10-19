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
    /// <summary>
    /// Moves to Starting(item). When <paramref name="stopEngine"/> is set that engine is stopped
    /// first and the start waits for its paused report, its track end, or 2 seconds.
    /// </summary>
    private async Task BeginStartAsync(
        PlaybackItem item,
        string inputName,
        string reason,
        EngineKind? stopEngine,
        CancellationToken ct,
        int? queueAheadMs = null
    )
    {
        _stopWait = null;
        _startDeadline = null;
        _pausedForHandOver = null;
        _linedUpBehindCurrentTrack = false;
        PlaybackState starting = PlaybackState.Starting(item.Engine, item);
        if (stopEngine is { } old)
        {
            // The owner's music is never paused for an engine that cannot start: it plays on, and the item
            // counts a failed start.
            if (old != item.Engine && !await CanStartOnAsync(item.Engine, ct))
            {
                await FailStartAsync(item, inputName, "engine not ready", "start_refused", ct);
                return;
            }

            if (!await CommandAsync(old, inputName, e => e.StopAsync(ct)))
                return;

            MarkSent(old);
            if (old != item.Engine)
                _pausedForHandOver = old;
            if (_ownerReport is not { IsPlaying: false } quiet || quiet.Engine != old)
            {
                await MoveAsync(starting, inputName, reason);
                _stopWait = old;
                _stopDeadline = _time.GetUtcNow() + StopWait;
                return;
            }
        }

        await MoveAsync(starting, inputName, reason);
        await SendStartAsync(item, inputName, ct, queueAheadMs);
    }

    /// <summary>
    /// Sends the start. With <paramref name="queueAheadMs"/> the owner's track still plays that long: the item
    /// is lined up behind it (once per item) and the start deadline runs from that track's end.
    /// </summary>
    private async Task SendStartAsync(
        PlaybackItem item,
        string inputName,
        CancellationToken ct,
        int? queueAheadMs = null
    )
    {
        if (!_engines.TryGetValue(item.Engine, out IPlaybackEngine? engine))
        {
            await FailStartAsync(item, inputName, "engine unavailable", "start_refused", ct);
            return;
        }

        EngineStartResult result = await SendToEngineOnceAsync(engine, item, queueAheadMs, ct);
        _linedUpBehindCurrentTrack = result.Accepted && queueAheadMs is not null;
        if (result.Accepted)
            _startDeadline =
                _time.GetUtcNow() + TimeSpan.FromMilliseconds(queueAheadMs ?? 0) + StartDeadline;
        else
            await FailStartAsync(
                item,
                inputName,
                result.FailureReason ?? "start refused",
                "start_refused",
                ct,
                engineBlocked: result.Block is not null
            );
    }

    private async Task OnStartingAsync(PlaybackInput input, CancellationToken ct)
    {
        PlaybackItem item = State.Item!;
        string name = input.GetType().Name;

        if (_stopWait is { } stopping)
        {
            bool confirmed = input switch
            {
                EngineReport { IsPlaying: false } r => r.Engine == stopping,
                TrackEnded t => t.Engine == stopping,
                _ => false,
            };
            if (confirmed)
            {
                _stopWait = null;
                await MoveAsync(State, name, "stop_confirmed");
                await SendStartAsync(item, name, ct);
            }
            else
            {
                await IgnoreAsync(input);
            }

            return;
        }

        switch (input)
        {
            case EngineReport { IsPlaying: true } r
                when r.Engine == item.Engine && r.Uri == item.Uri:
                await PlayRowAsync(item, r, "started");
                break;
            case EngineReport { IsPlaying: true } r when FindRow(r) is { } row:
                await PlayRowAsync(row, r, "played_out_of_order");
                break;
            case EngineReport { IsPlaying: true } r
                when r.Engine != item.Engine && FindRow(r) is null:
                await SilenceForeignAsync(r, ct);
                break;
            case OwnerSkip when _linedUpBehindCurrentTrack:
                await SkipToHeadAsync(item, name, ct);
                break;
            case TrackEnded t when t.Engine == item.Engine && t.Uri == item.Uri:
                await FailStartAsync(item, name, "player error", "start_failed", ct);
                break;
            case StartTimeout:
                await FailStartAsync(item, name, "did not start", "start_timeout", ct);
                break;
            default:
                await IgnoreAsync(input);
                break;
        }
    }

    /// <summary>Asks the engine whether a start could be sent now. An engine that is missing or throws answers no.</summary>
    private async Task<bool> CanStartOnAsync(EngineKind engine, CancellationToken ct)
    {
        if (!_engines.TryGetValue(engine, out IPlaybackEngine? target))
            return false;

        try
        {
            return await target.CanStartAsync(ct);
        }
        catch (Exception ex)
            when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>The queue row for a report, when that engine is playing a queued track.</summary>
    private PlaybackItem? FindRow(EngineReport report) =>
        _queue.Snapshot.FirstOrDefault(i => i.Engine == report.Engine && i.Uri == report.Uri);

    /// <summary>A queue row is playing: remove that one row, remember it, and play it.</summary>
    private async Task PlayRowAsync(PlaybackItem row, EngineReport report, string reason)
    {
        _queue.Remove(row.Id);
        _failedStarts.Remove(row.Id);
        _queuedAhead.Remove(row.Id);
        _history.Push(row);
        _ownerReport = null;
        _confirmedDeviceId = report.DeviceId;
        await MoveAsync(PlaybackState.Playing(report.Engine, row), report, reason);
    }
}
