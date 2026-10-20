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

    /// <summary>Items already lined up in the engine's own queue; a retry never lines one up twice.</summary>
    private readonly HashSet<string> _queuedAhead = [];

    /// <summary>Heads already announced as passed over for a missing player; an entry ends when the item plays or is dropped.</summary>
    private readonly HashSet<string> _passedOver = [];

    /// <summary>The owner's engine the current start paused for a hand-over to another engine; null when none.</summary>
    private EngineKind? _pausedForHandOver;

    /// <summary>
    /// Asks the engine to start the item. A refusal and a throw are the same thing to the director:
    /// a failed start. A cancel from the caller's token is not a failure and still propagates.
    /// </summary>
    private static async Task<EngineStartResult> TryStartAsync(
        IPlaybackEngine engine,
        PlaybackItem item,
        Func<IPlaybackEngine, Task<EngineStartResult>> start,
        CancellationToken ct
    )
    {
        try
        {
            return await start(engine);
        }
        catch (Exception ex)
            when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return EngineStartResult.Refused($"start failed: {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// Starts the item. An item the engine already holds in its own queue is not sent again: it plays there on
    /// its own.
    /// </summary>
    private async Task<EngineStartResult> SendToEngineOnceAsync(
        IPlaybackEngine engine,
        PlaybackItem item,
        CancellationToken ct
    )
    {
        if (_queuedAhead.Contains(item.Id))
            return EngineStartResult.Ok;

        EngineStartResult result = await TryStartAsync(
            engine,
            item,
            e => e.StartAsync(item, ct),
            ct
        );
        if (result.Accepted && engine.LinesUpInItsOwnQueue)
            _queuedAhead.Add(item.Id);

        return result;
    }

    /// <summary>
    /// While the owner's own music plays on, a failed start never cuts into it: the director follows that
    /// music and tries the item again at the end of the owner's track, this many times in all.
    /// </summary>
    private const int MaxFailedStartsOverOwnerMusic = 3;

    /// <summary>
    /// A failed start while the owner's music plays on follows that music and keeps the item. Otherwise the
    /// first failed start blocks the director and keeps the item. The last allowed failure drops the item
    /// with a notice. Only the director removes the item here. An engine that refuses with a typed block
    /// (no Music Player page open, Spotify not connected) always shows that block, after the music a hand-over
    /// paused plays on again, and never counts against the item: the item waits for the engine.
    /// </summary>
    private async Task FailStartAsync(
        PlaybackItem item,
        string inputName,
        string blockedReason,
        string journalReason,
        CancellationToken ct,
        bool engineBlocked = false
    )
    {
        EngineReport? handedOver = await OwnerMusicHandedOverAsync(item, inputName, ct);
        if (engineBlocked)
        {
            await MoveAsync(
                PlaybackState.Blocked(blockedReason),
                inputName,
                journalReason,
                item.Id
            );
            return;
        }

        int failures = _failedStarts.GetValueOrDefault(item.Id) + 1;
        EngineReport? ownerMusic = OwnerMusicPlayingOver(item) ?? handedOver;
        if (ownerMusic is not null && failures < MaxFailedStartsOverOwnerMusic)
        {
            _failedStarts[item.Id] = failures;
            await FollowOwnerMusicAsync(ownerMusic, inputName, journalReason, item.Id);
            return;
        }

        if (ownerMusic is null && failures < MaxFailedStarts)
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
        _queuedAhead.Remove(item.Id);
        _passedOver.Remove(item.Id);
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
        if (ownerMusic is not null)
        {
            await FollowOwnerMusicAsync(ownerMusic, inputName, "start_dropped", item.Id);
            return;
        }

        await MoveAsync(PlaybackState.Idle, inputName, "start_dropped", item.Id);

        if (_queue.Head is { } next)
            await BeginStartAsync(next, inputName, "next_item", null, ct);
    }

    /// <summary>
    /// The item's engine cannot take a start now. That is no fault of the item, so it counts no failed start
    /// and stays at the head: music another engine plays goes on, and the start is tried again when the
    /// engine's output attaches or that track ends.
    /// </summary>
    private async Task WaitForEngineAsync(PlaybackItem item, string inputName, string blockedReason)
    {
        if (
            _reports.Values.FirstOrDefault(r => r.IsPlaying && r.Engine != item.Engine) is { } music
        )
        {
            await FollowOwnerMusicAsync(music, inputName, "waiting_for_engine", item.Id);
            return;
        }

        await MoveAsync(
            PlaybackState.Blocked(blockedReason),
            inputName,
            "waiting_for_engine",
            item.Id
        );
    }

    /// <summary>The engine's latest report when it plays the owner's own track, not the item or a queued row.</summary>
    private EngineReport? OwnerMusicPlayingOver(PlaybackItem item) =>
        _reports.TryGetValue(item.Engine, out EngineReport? report)
        && report.IsPlaying
        && report.Uri != item.Uri
        && FindRow(report) is null
            ? report
            : null;

    /// <summary>
    /// The owner's music on another engine that this item was to take over from. Music the hand-over paused
    /// plays on again; music it never paused is still playing. Null when no owner music was handed over.
    /// </summary>
    private async Task<EngineReport?> OwnerMusicHandedOverAsync(
        PlaybackItem item,
        string inputName,
        CancellationToken ct
    )
    {
        EngineKind? paused = _pausedForHandOver;
        _pausedForHandOver = null;
        if (
            _memory is not { WasPlaying: true } memory
            || memory.Engine == item.Engine
            || !_reports.TryGetValue(memory.Engine, out EngineReport? last)
        )
            return null;

        if (paused == memory.Engine)
        {
            if (!await CommandAsync(memory.Engine, inputName, e => e.ResumeAsync(ct)))
                return null;

            MarkSent(memory.Engine);
        }

        return last with
        {
            IsPlaying = true,
        };
    }

    /// <summary>Owner music plays on: the director holds it, so the next hand-over waits for its end.</summary>
    private Task FollowOwnerMusicAsync(
        EngineReport ownerMusic,
        string inputName,
        string reason,
        string itemId
    )
    {
        _ownerReport = ownerMusic;
        _memory = null;
        return MoveAsync(PlaybackState.Playing(ownerMusic.Engine, null), inputName, reason, itemId);
    }
}
