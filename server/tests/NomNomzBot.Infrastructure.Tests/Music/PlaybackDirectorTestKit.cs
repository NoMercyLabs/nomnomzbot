// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Domain.Music.Exceptions;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Music.Playback;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>Shared doubles for the playback director tests: a recording engine, a recording event bus, a fake clock.</summary>
public static class PlaybackDirectorTestKit
{
    public static readonly Guid Channel = Guid.Parse("0192c000-0000-7000-8000-00000000d001");

    public static FakeTimeProvider NewClock() => new(new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));

    public static PlaybackItem Item(
        string id,
        EngineKind engine = EngineKind.Spotify,
        string owner = "alice",
        int durationMs = 200_000
    ) =>
        new(
            id,
            owner,
            engine,
            $"{engine}:{id}",
            $"Title {id}",
            ["Artist"],
            null,
            durationMs,
            owner
        );

    /// <summary>One recorded call on a <see cref="FakeEngine"/>, in the order the engine received it.</summary>
    public sealed record EngineCall(string Name, string? Argument = null);

    public sealed class FakeEngine(EngineKind kind) : IPlaybackEngine
    {
        private readonly List<EngineCall> _calls = [];

        public EngineKind Kind { get; } = kind;

        /// <summary>What <see cref="StartAsync"/> answers. Defaults to accepted.</summary>
        public EngineStartResult StartResult { get; set; } = EngineStartResult.Ok;

        /// <summary>When set, <see cref="StartAsync"/> throws what this returns.</summary>
        public Func<Exception>? StartThrows { get; set; }

        /// <summary>When set, <see cref="StartAsync"/> runs and awaits this before it answers (a test reads ambient state or holds the start open).</summary>
        public Func<PlaybackItem, Task>? DuringStart { get; set; }

        /// <summary>When set, Pause, Resume, Stop, Skip, Seek and SetVolume throw the typed block and a start answers it, as a real engine does.</summary>
        public EngineBlockReason? BlockCommands { get; set; }

        public IReadOnlyList<EngineCall> Calls => _calls;

        public event Func<EngineSignal, Task>? Reports;

        public Task<EngineStartResult> StartAsync(PlaybackItem item, CancellationToken ct = default)
        {
            _calls.Add(new("Start", item.Id));

            return Answer(item);
        }

        /// <summary>A blocked engine answers a start with its block, as the real engines' guard does.</summary>
        private Task<EngineStartResult> Answer(PlaybackItem item)
        {
            if (StartThrows is not null)
                throw StartThrows();
            if (BlockCommands is { } reason)
                return Task.FromResult(EngineStartResult.BlockedBy(reason));

            return DuringStart is null ? Task.FromResult(StartResult) : StartAfterAsync(item);
        }

        /// <summary>The fake Spotify keeps a started item in its own queue, as the real one does.</summary>
        public bool LinesUpInItsOwnQueue => Kind == EngineKind.Spotify;

        public Task<EngineStartResult> QueueNextAsync(
            PlaybackItem item,
            CancellationToken ct = default
        )
        {
            _calls.Add(new("QueueNext", item.Id));

            return Answer(item);
        }

        private async Task<EngineStartResult> StartAfterAsync(PlaybackItem item)
        {
            await DuringStart!(item);

            return StartResult;
        }

        public Task PauseAsync(CancellationToken ct = default) => Record("Pause");

        public Task ResumeAsync(CancellationToken ct = default) => Record("Resume");

        public Task StopAsync(CancellationToken ct = default) => Record("Stop");

        /// <summary>The fake Spotify moves its own queue on; an engine without one stops, as the real ones do.</summary>
        public Task SkipAsync(CancellationToken ct = default) =>
            LinesUpInItsOwnQueue ? Record("Skip") : StopAsync(ct);

        /// <summary>What <see cref="CanStartAsync"/> answers. Defaults to ready.</summary>
        public bool CanStart { get; set; } = true;

        /// <summary>A read, not a command: kept out of <see cref="Calls"/>.</summary>
        public Task<bool> CanStartAsync(CancellationToken ct = default) =>
            Task.FromResult(CanStart);

        /// <summary>How often the director lifted this engine's own output pause.</summary>
        public int OutputBlockForgotten { get; private set; }

        public void ForgetOutputBlock() => OutputBlockForgotten++;

        public Task SeekAsync(int positionMs, CancellationToken ct = default) =>
            Record("Seek", positionMs.ToString());

        public Task SetVolumeAsync(int level, CancellationToken ct = default) =>
            Record("SetVolume", level.ToString());

        public Task ReattachOutputAsync(bool pageIsPlaying, CancellationToken ct = default) =>
            Record("Reattach", pageIsPlaying.ToString());

        /// <summary>What <see cref="ProbeAsync"/> answers: what the player plays before any report. Null means nothing.</summary>
        public EngineReport? ProbeReport { get; set; }

        /// <summary>How often the director probed this engine. Kept out of <see cref="Calls"/>: a probe is a read, not a command.</summary>
        public int ProbeCount { get; private set; }

        public Task<EngineReport?> ProbeAsync(CancellationToken ct = default)
        {
            ProbeCount++;

            return Task.FromResult(ProbeReport);
        }

        /// <summary>Makes the engine emit a report or track end, as its poll or player would.</summary>
        public Task EmitAsync(EngineSignal signal) =>
            Reports is null ? Task.CompletedTask : Reports.Invoke(signal);

        private Task Record(string name, string? argument = null)
        {
            _calls.Add(new(name, argument));
            if (BlockCommands is { } reason)
                throw new PlaybackEngineBlockedException(Kind, reason);

            return Task.CompletedTask;
        }
    }

    /// <summary>Hands out one pair of <see cref="FakeEngine"/>s per channel and counts the pairs it built.</summary>
    public sealed class FakeEngineFactory : IPlaybackEngineFactory
    {
        private readonly Dictionary<Guid, FakeEngine[]> _pairs = [];

        public int CreateCount { get; private set; }

        public FakeEngine EngineOf(Guid broadcasterId, EngineKind kind) =>
            _pairs[broadcasterId].Single(engine => engine.Kind == kind);

        public IReadOnlyList<IPlaybackEngine> Create(Guid broadcasterId)
        {
            CreateCount++;
            FakeEngine[] pair = [new(EngineKind.Spotify), new(EngineKind.YouTube)];
            _pairs[broadcasterId] = pair;

            return pair;
        }
    }

    /// <summary>A registry over fake engines, for tests that only feed reports and read the queue.</summary>
    public static PlaybackDirectorRegistry Registry(ISongRequestQueueStore store, IEventBus bus) =>
        new(store, new FakeEngineFactory(), bus, TimeProvider.System);

    public sealed class RecordingBus : IEventBus
    {
        public List<IDomainEvent> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
            where TEvent : class, IDomainEvent
        {
            Published.Add(@event);
            return Task.CompletedTask;
        }

        public void PublishFireAndForget<TEvent>(TEvent @event)
            where TEvent : class, IDomainEvent => Published.Add(@event);
    }
}
