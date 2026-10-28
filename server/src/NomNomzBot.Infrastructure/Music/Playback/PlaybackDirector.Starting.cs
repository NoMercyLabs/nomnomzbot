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

using NomNomzBot.Domain.Music.Events;
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
        PlaybackItem head,
        string inputName,
        string reason,
        EngineKind? stopEngine,
        CancellationToken ct
    )
    {
        PlaybackItem item = await StartableOrHeadAsync(head, ct);
        _stopWait = null;
        _startDeadline = null;
        _pausedForHandOver = null;
        PlaybackState starting = PlaybackState.Starting(item.Engine, item);
        if (stopEngine is { } old)
        {
            // The owner's music is never paused for an engine that cannot start: it plays on, and the item
            // waits for that engine.
            if (old != item.Engine && !await CanStartOnAsync(item.Engine, ct))
            {
                await WaitForEngineAsync(item, inputName, "engine not ready");
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
        await SendStartAsync(item, inputName, ct);
    }

    /// <summary>
    /// The one stall guard. A head whose engine has nothing to play on (a YouTube request with no Music Player
    /// page open) never holds the requests behind it: the first request behind it that can start is started
    /// instead, and the head stays queued for the page. Once per head, <see cref="SongRequestPassedOverEvent"/>
    /// says so. When nothing behind can start either, the head is returned and waits as before.
    /// </summary>
    private async Task<PlaybackItem> StartableOrHeadAsync(PlaybackItem head, CancellationToken ct)
    {
        if (await CanStartOnAsync(head.Engine, ct))
            return head;

        PlaybackItem? behind = await StartableBehindAsync(head, ct);
        if (behind is null)
            return head;

        await AnnouncePassedOverAsync(head, ct);

        return behind;
    }

    /// <summary>The first request behind a head that cannot start whose engine can; null when none can.</summary>
    private async Task<PlaybackItem?> StartableBehindAsync(PlaybackItem head, CancellationToken ct)
    {
        foreach (PlaybackItem candidate in _queue.Snapshot)
            if (candidate.Engine != head.Engine && await CanStartOnAsync(candidate.Engine, ct))
                return candidate;

        return null;
    }

    /// <summary>Publishes <see cref="SongRequestPassedOverEvent"/> once per waiting head.</summary>
    private async Task AnnouncePassedOverAsync(PlaybackItem head, CancellationToken ct)
    {
        if (!_passedOver.Add(head.Id))
            return;

        await _bus.PublishAsync(
            new SongRequestPassedOverEvent
            {
                BroadcasterId = _broadcasterId,
                OccurredAt = _time.GetUtcNow(),
                TrackUri = head.Uri,
                TrackName = head.Title,
            },
            ct
        );
    }

    /// <summary>Sends the start; the engine then has <see cref="StartDeadline"/> to report the item playing.</summary>
    private async Task SendStartAsync(PlaybackItem item, string inputName, CancellationToken ct)
    {
        if (!_engines.TryGetValue(item.Engine, out IPlaybackEngine? engine))
        {
            await FailStartAsync(item, inputName, "engine unavailable", "start_refused", ct);
            return;
        }

        EngineStartResult result = await SendToEngineOnceAsync(engine, item, ct);
        if (result.Accepted)
            _startDeadline = _time.GetUtcNow() + StartDeadline;
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
            case TrackEnded t when t.Engine == item.Engine && t.Uri == item.Uri:
                await FailStartAsync(item, name, "player error", "start_failed", ct);
                break;
            case StartTimeout when await IsStillQueuedAsync(item, ct):
                _startDeadline = _time.GetUtcNow() + StartDeadline;
                await MoveAsync(State, name, "start_still_queued", item.Id);
                break;
            case StartTimeout:
                await FailStartAsync(item, name, "did not start", "start_timeout", ct);
                break;
            default:
                await IgnoreAsync(input);
                break;
        }
    }

    /// <summary>
    /// Whether the item's engine still holds it in its own queue. A start the engine has not confirmed yet is
    /// pending there, not failed: Spotify plays a queued song only after the track before it ends. An engine
    /// that is missing, has no queue or throws answers no.
    /// </summary>
    private async Task<bool> IsStillQueuedAsync(PlaybackItem item, CancellationToken ct)
    {
        if (!_engines.TryGetValue(item.Engine, out IPlaybackEngine? engine))
            return false;
        if (!engine.LinesUpInItsOwnQueue)
            return false;

        try
        {
            return await engine.IsStillQueuedAsync(item, ct);
        }
        catch (Exception ex)
            when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return false;
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
        _passedOver.Remove(row.Id);
        _history.Push(row);
        _ownerReport = null;
        _confirmedDeviceId = report.DeviceId;
        await MoveAsync(PlaybackState.Playing(report.Engine, row), report, reason);
    }
}
