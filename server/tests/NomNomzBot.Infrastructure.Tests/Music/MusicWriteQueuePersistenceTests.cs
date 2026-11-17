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
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Entities;
using NomNomzBot.Infrastructure.Music;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>Held favorite and banger writes survive a restart: they are stored when held and removed once sent.</summary>
public sealed class MusicWriteQueuePersistenceTests : IDisposable
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-000000009933");
    private static readonly Guid OtherBroadcaster = Guid.Parse(
        "0192a000-0000-7000-8000-000000009944"
    );
    private const string FirstUri = "youtube:video:first";
    private const string SecondUri = "youtube:video:second";

    private readonly IMusicProviderManageApi _provider = Substitute.For<IMusicProviderManageApi>();
    private readonly FakeTimeProvider _clock = new(MusicWriteQueueTestKit.Start);
    private readonly SqliteConnection _database = MusicWriteQueueTestKit.NewDatabase();

    public MusicWriteQueuePersistenceTests()
    {
        _provider
            .SaveTracksAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
    }

    public void Dispose() => _database.Dispose();

    private static MusicWriteRequest Save(Guid broadcaster, string uri) =>
        new(broadcaster, "youtube", MusicWriteKind.Save, [uri]);

    private MusicWriteQueue CoolingQueue()
    {
        YouTubeQuotaCooldown quota = new(_clock);
        MusicWriteQueue queue = MusicWriteQueueTestKit.Over(_provider, _clock, quota, _database);
        quota.CoolUntilQuotaReset();
        return queue;
    }

    [Fact]
    public async Task A_held_write_survives_a_restart_and_is_sent_once_then_its_row_is_gone()
    {
        MusicWriteQueue beforeRestart = CoolingQueue();
        Result<MusicWriteOutcome> outcome = await beforeRestart.SubmitAsync(
            Save(Broadcaster, FirstUri)
        );

        outcome.Value.Should().Be(MusicWriteOutcome.Held);
        List<MusicHeldWrite> stored = MusicWriteQueueTestKit.HeldRows(_database);
        stored.Should().ContainSingle();
        stored[0].BroadcasterId.Should().Be(Broadcaster);
        stored[0].Provider.Should().Be("youtube");
        stored[0].Kind.Should().Be("Save");
        stored[0].TrackUrisJson.Should().Contain(FirstUri);

        MusicWriteQueue afterRestart = MusicWriteQueueTestKit.Over(
            _provider,
            _clock,
            database: _database
        );
        int sent = await afterRestart.FlushAsync(Broadcaster);

        sent.Should().Be(1);
        await _provider
            .Received(1)
            .SaveTracksAsync(
                Broadcaster,
                "youtube",
                Arg.Is<IReadOnlyList<string>>(u => u.SequenceEqual(new[] { FirstUri })),
                Arg.Any<CancellationToken>()
            );
        MusicWriteQueueTestKit.HeldRows(_database).Should().BeEmpty();
        (await afterRestart.FlushAsync(Broadcaster)).Should().Be(0);
    }

    [Fact]
    public async Task A_write_that_goes_out_at_once_leaves_no_row()
    {
        MusicWriteQueue queue = MusicWriteQueueTestKit.Over(_provider, _clock, database: _database);

        Result<MusicWriteOutcome> outcome = await queue.SubmitAsync(Save(Broadcaster, FirstUri));

        outcome.Value.Should().Be(MusicWriteOutcome.Sent);
        MusicWriteQueueTestKit.HeldRows(_database).Should().BeEmpty();
    }

    [Fact]
    public async Task A_failing_held_write_keeps_its_row_and_does_not_block_the_others()
    {
        _provider
            .SaveTracksAsync(
                Broadcaster,
                "youtube",
                Arg.Is<IReadOnlyList<string>>(u => u.Contains(FirstUri)),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("down", "SERVICE_UNAVAILABLE"));
        MusicWriteQueue queue = CoolingQueue();
        await queue.SubmitAsync(Save(Broadcaster, FirstUri));
        _clock.Advance(TimeSpan.FromMinutes(1));
        await queue.SubmitAsync(Save(Broadcaster, SecondUri));
        MusicWriteQueue afterRestart = MusicWriteQueueTestKit.Over(
            _provider,
            _clock,
            database: _database
        );

        int sent = await afterRestart.FlushAsync(Broadcaster);

        sent.Should().Be(1);
        List<MusicHeldWrite> left = MusicWriteQueueTestKit.HeldRows(_database);
        left.Should().ContainSingle();
        left[0].TrackUrisJson.Should().Contain(FirstUri);
    }

    [Fact]
    public async Task Flushing_all_channels_after_a_restart_sends_every_channels_held_writes()
    {
        MusicWriteQueue beforeRestart = CoolingQueue();
        await beforeRestart.SubmitAsync(Save(Broadcaster, FirstUri));
        await beforeRestart.SubmitAsync(Save(OtherBroadcaster, SecondUri));
        MusicWriteQueue afterRestart = MusicWriteQueueTestKit.Over(
            _provider,
            _clock,
            database: _database
        );

        int sent = await afterRestart.FlushAllAsync();

        sent.Should().Be(2);
        MusicWriteQueueTestKit.HeldRows(_database).Should().BeEmpty();
        await _provider
            .Received(1)
            .SaveTracksAsync(
                OtherBroadcaster,
                "youtube",
                Arg.Is<IReadOnlyList<string>>(u => u.SequenceEqual(new[] { SecondUri })),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_flush_of_one_channel_leaves_another_channels_row_alone()
    {
        MusicWriteQueue queue = CoolingQueue();
        await queue.SubmitAsync(Save(Broadcaster, FirstUri));
        await queue.SubmitAsync(Save(OtherBroadcaster, SecondUri));
        MusicWriteQueue afterRestart = MusicWriteQueueTestKit.Over(
            _provider,
            _clock,
            database: _database
        );

        await afterRestart.FlushAsync(Broadcaster);

        List<MusicHeldWrite> left = MusicWriteQueueTestKit.HeldRows(_database);
        left.Should().ContainSingle();
        left[0].BroadcasterId.Should().Be(OtherBroadcaster);
    }
}
