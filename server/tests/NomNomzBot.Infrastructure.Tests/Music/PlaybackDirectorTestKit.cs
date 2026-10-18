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
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;

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

        public IReadOnlyList<EngineCall> Calls => _calls;

        public event Func<EngineSignal, Task>? Reports;

        public Task<EngineStartResult> StartAsync(PlaybackItem item, CancellationToken ct = default)
        {
            _calls.Add(new("Start", item.Id));
            if (StartThrows is not null)
                throw StartThrows();

            return Task.FromResult(StartResult);
        }

        public Task PauseAsync(CancellationToken ct = default) => Record("Pause");

        public Task ResumeAsync(CancellationToken ct = default) => Record("Resume");

        public Task StopAsync(CancellationToken ct = default) => Record("Stop");

        public Task SeekAsync(int positionMs, CancellationToken ct = default) =>
            Record("Seek", positionMs.ToString());

        public Task SetVolumeAsync(int level, CancellationToken ct = default) =>
            Record("SetVolume", level.ToString());

        /// <summary>Makes the engine emit a report or track end, as its poll or player would.</summary>
        public Task EmitAsync(EngineSignal signal) =>
            Reports is null ? Task.CompletedTask : Reports.Invoke(signal);

        private Task Record(string name, string? argument = null)
        {
            _calls.Add(new(name, argument));
            return Task.CompletedTask;
        }
    }

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
