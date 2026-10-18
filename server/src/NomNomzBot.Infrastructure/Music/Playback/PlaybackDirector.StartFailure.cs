// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Music.Playback;

public sealed partial class PlaybackDirector
{
    /// <summary>An item that fails to start this many times is dropped (music-playback-determinism.md section 6).</summary>
    private const int MaxFailedStarts = 2;

    /// <summary>Failed starts per queue item id. An entry ends when the item plays or is dropped.</summary>
    private readonly Dictionary<string, int> _failedStarts = [];

    /// <summary>
    /// Asks the engine to start the item. A refusal and a throw are the same thing to the director:
    /// a failed start. A cancel from the caller's token is not a failure and still propagates.
    /// </summary>
    private async Task<EngineStartResult> TryStartAsync(
        IPlaybackEngine engine,
        PlaybackItem item,
        CancellationToken ct
    )
    {
        try
        {
            return await engine.StartAsync(item, ct);
        }
        catch (Exception ex)
            when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return EngineStartResult.Refused($"start failed: {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// The first failed start of an item blocks the director and keeps the item. The second drops the
    /// item with a notice and starts the next one. Only the director removes the item here.
    /// </summary>
    private async Task FailStartAsync(
        PlaybackItem item,
        string inputName,
        string blockedReason,
        string journalReason,
        CancellationToken ct
    )
    {
        int failures = _failedStarts.GetValueOrDefault(item.Id) + 1;
        if (failures < MaxFailedStarts)
        {
            _failedStarts[item.Id] = failures;
            await MoveAsync(
                PlaybackState.Blocked(blockedReason),
                inputName,
                journalReason,
                item.Id
            );
            return;
        }

        _failedStarts.Remove(item.Id);
        await _bus.PublishAsync(
            new SongPlaybackFailedEvent
            {
                BroadcasterId = _broadcasterId,
                OccurredAt = _time.GetUtcNow(),
                TrackUri = item.Uri,
                TrackName = item.Title,
            },
            ct
        );
        _queue.Remove(item.Id);
        await MoveAsync(PlaybackState.Idle, inputName, "start_dropped", item.Id);

        if (_queue.Head is { } next)
            await BeginStartAsync(next, inputName, "next_item", null, ct);
    }
}
