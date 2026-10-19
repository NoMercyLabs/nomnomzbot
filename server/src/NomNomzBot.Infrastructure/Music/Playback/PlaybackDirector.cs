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
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Music.Playback;

/// <summary>
/// The one owner of playback state for a channel (music-playback-determinism.md section 2).
/// Engines report, the queue says what is next, and this class decides. One input runs at a time;
/// every state change is journaled as one <see cref="PlaybackTransitionedEvent"/>.
/// </summary>
public sealed partial class PlaybackDirector : IDisposable
{
    /// <summary>Remaining time of the owner's track at which a waiting request takes over.</summary>
    private const int NearEndMs = 6_000;

    private static readonly TimeSpan StartDeadline = TimeSpan.FromSeconds(10);

    /// <summary>Progress at or under which Previous goes to the last played item instead of the start.</summary>
    private const int RestartThresholdMs = 3_000;

    /// <summary>A report this soon after a command of ours is the echo of that command, not the owner.</summary>
    private static readonly TimeSpan OwnerEcho = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan StopWait = TimeSpan.FromSeconds(2);

    private readonly Guid _broadcasterId;
    private readonly Dictionary<EngineKind, IPlaybackEngine> _engines;
    private readonly IPlaybackQueue _queue;
    private readonly TimeProvider _time;
    private readonly IEventBus _bus;
    private readonly PlayedHistory _history = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<EngineSignal, Task> _onSignal;

    private readonly Dictionary<EngineKind, EngineReport> _reports = [];
    private readonly Dictionary<EngineKind, DateTimeOffset> _lastSent = [];

    /// <summary>Engines already read once by <see cref="FindOwnerMusicAsync"/>; each is probed at most once.</summary>
    private readonly HashSet<EngineKind> _probed = [];
    private int _volume = 100;
    private bool _muted;

    private EngineReport? _ownerReport;
    private string? _confirmedDeviceId;
    private OwnerMusicMemory? _memory;
    private EngineKind? _stopWait;
    private DateTimeOffset _stopDeadline;
    private DateTimeOffset? _startDeadline;

    public PlaybackDirector(
        Guid broadcasterId,
        IEnumerable<IPlaybackEngine> engines,
        IPlaybackQueue queue,
        TimeProvider time,
        IEventBus bus
    )
    {
        _broadcasterId = broadcasterId;
        _engines = engines.ToDictionary(e => e.Kind);
        _queue = queue;
        _time = time;
        _bus = bus;
        _onSignal = signal => HandleAsync(signal);
        foreach (IPlaybackEngine engine in _engines.Values)
            engine.Reports += _onSignal;
    }

    public PlaybackState State { get; private set; } = PlaybackState.Idle;

    /// <summary>The item that most recently started playing, or null when none has.</summary>
    public PlaybackItem? LastPlayed => _history.Last;

    public async Task HandleAsync(PlaybackInput input, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await ApplyAsync(input, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Raises the stop-wait and start deadlines that have passed. A timer calls it.</summary>
    public async Task CheckDeadlinesAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (State.Kind != PlaybackStateKind.Starting)
                return;

            DateTimeOffset now = _time.GetUtcNow();
            if (_stopWait is not null && now >= _stopDeadline)
            {
                _stopWait = null;
                await MoveAsync(State, "StopTimeout", "stop_unconfirmed");
                await SendStartAsync(State.Item!, "StopTimeout", ct);
            }
            else if (_startDeadline is { } deadline && now >= deadline)
            {
                await ApplyAsync(new StartTimeout(), ct);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        foreach (IPlaybackEngine engine in _engines.Values)
            engine.Reports -= _onSignal;
        _gate.Dispose();
    }

    private Task ApplyAsync(PlaybackInput input, CancellationToken ct)
    {
        if (input is EngineReport report)
            _reports[report.Engine] = report;

        return Dispatch(input, ct);
    }

    private Task Dispatch(PlaybackInput input, CancellationToken ct) =>
        State.Kind switch
        {
            PlaybackStateKind.PausedByOwner => OnPausedAsync(input, ct),
            PlaybackStateKind.Idle => OnIdleAsync(input, ct),
            PlaybackStateKind.Starting => OnStartingAsync(input, ct),
            PlaybackStateKind.Playing => OnPlayingAsync(input, ct),
            PlaybackStateKind.Blocked => OnBlockedAsync(input, ct),
            _ => IgnoreAsync(input),
        };

    /// <summary>Sets the new state and journals it. Every state change goes through here.</summary>
    private async Task MoveAsync(
        PlaybackState to,
        string inputName,
        string reason,
        string? itemId = null
    )
    {
        PlaybackState from = State;
        State = to;
        if (to.Kind != PlaybackStateKind.Starting)
        {
            _stopWait = null;
            _startDeadline = null;
        }

        await _bus.PublishAsync(
            new PlaybackTransitionedEvent
            {
                BroadcasterId = _broadcasterId,
                OccurredAt = _time.GetUtcNow(),
                From = from.Kind.ToString(),
                To = to.Kind.ToString(),
                Input = inputName,
                Engine = to.Engine?.ToString(),
                QueueItemId = itemId ?? to.Item?.Id,
                Reason = reason,
            }
        );
    }

    private Task MoveAsync(PlaybackState to, PlaybackInput input, string reason) =>
        MoveAsync(to, input.GetType().Name, reason);

    /// <summary>An input the table does not list. A poll report is not journaled (it would flood).</summary>
    private Task IgnoreAsync(PlaybackInput input) =>
        input is EngineReport ? Task.CompletedTask : MoveAsync(State, input, "ignored");

    /// <summary>What was playing on an engine before a queue item took over (rules 12 and 13).</summary>
    private sealed record OwnerMusicMemory(EngineKind Engine, bool WasPlaying);
}
