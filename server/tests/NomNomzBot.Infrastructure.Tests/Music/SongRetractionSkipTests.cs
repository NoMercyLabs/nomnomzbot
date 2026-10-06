// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Spotify cannot delete a track from its own queue, so a request retracted after it was handed over
/// still plays. These tests pin the marker that makes the bot skip it when it starts: armed only by
/// removing the in-flight entry, consumed once, and forgotten after two hours.
/// </summary>
public sealed class SongRetractionSkipTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-00000000ab02");
    private const string TrackUri = "spotify:track:wrong";

    private static SongRequestEntry Entry(string trackUri) =>
        new(trackUri, "Track", "Artist", null, 200_000, "viewer", 0, null, "");

    private static PlaybackStateChangedEvent PlaybackOf(string trackUri) =>
        new()
        {
            BroadcasterId = ChannelId,
            IsPlaying = true,
            TrackUri = trackUri,
            TrackName = trackUri,
            Provider = "spotify",
            ObservedAt = DateTimeOffset.UtcNow,
        };

    private static MusicService BuildService(SongRequestQueueStore store)
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        IMusicProvider provider = Substitute.For<IMusicProvider>();
        provider.Provider.Returns("spotify");
        return new(
            [provider],
            db,
            new RecordingEventBus(),
            new BlockedTrackService(db),
            store,
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            PermissiveMusicConfigService.Instance,
            Substitute.For<ICurrencyAccountService>(),
            new NowPlayingCache(),
            new OutboundSanctionAccessor(),
            Substitute.For<IUserIdentityService>(),
            Substitute.For<IForeignLinkTitleLookup>()
        );
    }

    private static (SongRetractionSkipHandler Sut, IMusicService Music) BuildHandler(
        SongRequestQueueStore store
    )
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .SkipAsync(
                ChannelId.ToString(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        return (new(store, music, NullLogger<SongRetractionSkipHandler>.Instance), music);
    }

    private static int SkipCalls(IMusicService music) =>
        music.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IMusicService.SkipAsync));

    [Fact]
    public async Task Removing_the_in_flight_entry_then_its_start_skips_exactly_once()
    {
        SongRequestQueueStore store = new();
        string channel = ChannelId.ToString();
        SongRequestEntry entry = Entry(TrackUri);
        store.GetOrCreate(channel).Enqueue("viewer", entry);
        store.SetInFlight(channel, entry);
        MusicService service = BuildService(store);
        (SongRetractionSkipHandler sut, IMusicService music) = BuildHandler(store);

        (await service.RemoveFromQueueAsync(channel, 0)).Should().BeTrue();
        await sut.HandleAsync(PlaybackOf(TrackUri));

        SkipCalls(music).Should().Be(1);

        // The poller repeats the same state every tick: the marker is spent, nothing skips again.
        await sut.HandleAsync(PlaybackOf(TrackUri));
        SkipCalls(music).Should().Be(1);
    }

    [Fact]
    public async Task A_second_play_of_the_same_song_after_the_marker_was_consumed_is_not_skipped()
    {
        SongRequestQueueStore store = new();
        string channel = ChannelId.ToString();
        store.ArmRetraction(channel, TrackUri);
        (SongRetractionSkipHandler sut, IMusicService music) = BuildHandler(store);

        await sut.HandleAsync(PlaybackOf(TrackUri));
        await sut.HandleAsync(PlaybackOf("spotify:track:other"));
        await sut.HandleAsync(PlaybackOf(TrackUri));

        SkipCalls(music).Should().Be(1, "only the first start was retracted");
        store.TryConsumeRetraction(channel, TrackUri).Should().BeFalse();
    }

    [Fact]
    public async Task An_expired_marker_does_not_skip()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));
        SongRequestQueueStore store = new(clock);
        string channel = ChannelId.ToString();
        store.ArmRetraction(channel, TrackUri);
        (SongRetractionSkipHandler sut, IMusicService music) = BuildHandler(store);

        clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(1));
        await sut.HandleAsync(PlaybackOf(TrackUri));

        SkipCalls(music).Should().Be(0);
        store
            .TryConsumeRetraction(channel, TrackUri)
            .Should()
            .BeFalse("the expired marker was dropped, not kept for later");
    }

    [Fact]
    public async Task Removing_an_entry_that_is_not_in_flight_arms_nothing()
    {
        SongRequestQueueStore store = new();
        string channel = ChannelId.ToString();
        SongRequestEntry atProvider = Entry("spotify:track:head");
        SongRequestEntry waiting = Entry(TrackUri);
        FairQueue<SongRequestEntry> queue = store.GetOrCreate(channel);
        queue.Enqueue("viewer-a", atProvider);
        queue.Enqueue("viewer-b", waiting);
        store.SetInFlight(channel, atProvider);
        MusicService service = BuildService(store);
        (SongRetractionSkipHandler sut, IMusicService music) = BuildHandler(store);

        (await service.RemoveFromQueueAsync(channel, 1)).Should().BeTrue();
        await sut.HandleAsync(PlaybackOf(TrackUri));

        SkipCalls(music).Should().Be(0);
        store.GetInFlight(channel).Should().BeSameAs(atProvider);
    }

    [Fact]
    public async Task A_marker_is_per_channel()
    {
        SongRequestQueueStore store = new();
        store.ArmRetraction(Guid.NewGuid().ToString(), TrackUri);
        (SongRetractionSkipHandler sut, IMusicService music) = BuildHandler(store);

        await sut.HandleAsync(PlaybackOf(TrackUri));

        SkipCalls(music).Should().Be(0);
    }

    [Fact]
    public async Task The_queue_snapshot_marks_only_the_entry_at_the_provider_as_in_flight()
    {
        SongRequestQueueStore store = new();
        string channel = ChannelId.ToString();
        SongRequestEntry head = Entry("spotify:track:head");
        FairQueue<SongRequestEntry> queue = store.GetOrCreate(channel);
        queue.Enqueue("viewer-a", head);
        queue.Enqueue("viewer-b", Entry(TrackUri));
        store.SetInFlight(channel, head);
        MusicService service = BuildService(store);

        MusicQueue snapshot = await service.GetQueueAsync(channel);

        snapshot.Queue.Select(i => i.InFlight).Should().Equal(true, false);
    }
}
