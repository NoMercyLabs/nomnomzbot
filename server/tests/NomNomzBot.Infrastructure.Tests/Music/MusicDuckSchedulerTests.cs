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

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music.Playback;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Bot audio ducks the music for as long as it speaks (S-PD-8b): one DuckStart when speech begins, one DuckEnd
/// when the last queued line has finished. The overlay plays lines strictly in order, so overlapping lines add up.
/// </summary>
public sealed class MusicDuckSchedulerTests
{
    private static readonly Guid Channel = Guid.Parse("019f3000-0000-7000-8000-000000000001");
    private static readonly Guid Other = Guid.Parse("019f3000-0000-7000-8000-000000000002");

    private sealed class RecordingSink : IPlaybackInputSink
    {
        public List<(Guid Channel, PlaybackInput Input)> Sent { get; } = [];

        public Task<PlaybackInputOutcome> SendAsync(
            Guid broadcasterId,
            PlaybackInput input,
            CancellationToken ct = default
        )
        {
            Sent.Add((broadcasterId, input));
            return Task.FromResult(PlaybackInputOutcome.Applied);
        }
    }

    private static (MusicDuckScheduler Scheduler, RecordingSink Sink, FakeTimeProvider Time) Build()
    {
        RecordingSink sink = new();
        FakeTimeProvider time = new();
        return (new(sink, time, NullLogger<MusicDuckScheduler>.Instance), sink, time);
    }

    [Fact]
    public async Task A_line_ducks_the_music_at_once_and_restores_it_when_the_line_has_finished()
    {
        (MusicDuckScheduler scheduler, RecordingSink sink, FakeTimeProvider time) = Build();

        await scheduler.DuckAsync(Channel, 40, 5000);

        sink.Sent.Should().Equal((Channel, new DuckStart(40)));
        time.Advance(TimeSpan.FromMilliseconds(4999));
        sink.Sent.Should().HaveCount(1);
        time.Advance(TimeSpan.FromMilliseconds(1));
        sink.Sent.Should().Equal((Channel, new DuckStart(40)), (Channel, new DuckEnd()));
    }

    [Fact]
    public async Task Lines_that_queue_up_hold_one_duck_until_the_last_line_has_finished()
    {
        (MusicDuckScheduler scheduler, RecordingSink sink, FakeTimeProvider time) = Build();

        await scheduler.DuckAsync(Channel, 40, 5000);
        await scheduler.DuckAsync(Channel, 40, 3000);

        sink.Sent.Should().Equal((Channel, new DuckStart(40)));
        time.Advance(TimeSpan.FromMilliseconds(5000));
        sink.Sent.Should().HaveCount(1, "the second line is still playing");
        time.Advance(TimeSpan.FromMilliseconds(3000));
        sink.Sent.Should().Equal((Channel, new DuckStart(40)), (Channel, new DuckEnd()));
    }

    [Fact]
    public async Task A_channel_without_ducking_or_a_line_without_length_sends_nothing()
    {
        (MusicDuckScheduler scheduler, RecordingSink sink, FakeTimeProvider time) = Build();

        await scheduler.DuckAsync(Channel, 100, 5000);
        await scheduler.DuckAsync(Channel, 40, 0);
        time.Advance(TimeSpan.FromMinutes(1));

        sink.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Two_channels_duck_and_restore_independently()
    {
        (MusicDuckScheduler scheduler, RecordingSink sink, FakeTimeProvider time) = Build();

        await scheduler.DuckAsync(Channel, 20, 1000);
        await scheduler.DuckAsync(Other, 60, 4000);
        time.Advance(TimeSpan.FromMilliseconds(1000));

        sink.Sent.Should()
            .Equal(
                (Channel, new DuckStart(20)),
                (Other, new DuckStart(60)),
                (Channel, new DuckEnd())
            );
    }
}
