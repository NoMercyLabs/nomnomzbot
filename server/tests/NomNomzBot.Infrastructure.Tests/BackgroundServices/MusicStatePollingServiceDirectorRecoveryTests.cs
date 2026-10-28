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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.BackgroundServices;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tests.Music;
using NSubstitute;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.BackgroundServices;

/// <summary>
/// The poll tick is a recovery tick for a director that sits Idle while a request waits: it sends the director
/// RequestAdded, and the director starts the head on its engine. The director is the only way a head reaches an engine.
/// </summary>
public sealed class MusicStatePollingServiceDirectorRecoveryTests
{
    private static readonly Guid RecoveryChannel = Guid.Parse(
        "0192a000-0000-7000-8000-0000000f2001"
    );

    [Fact]
    public async Task A_poll_tick_with_an_Idle_director_and_a_waiting_head_starts_that_head_once_through_the_director()
    {
        Harness harness = Harness.WithWaitingHead();

        await harness.Sut.PollAllChannelsOnceAsync(CancellationToken.None);

        await harness
            .Music.Received(1)
            .GetNowPlayingAsync(RecoveryChannel.ToString(), Arg.Any<CancellationToken>(), true);
        harness
            .Spotify.Calls.Where(c => c.Name == "Start")
            .Should()
            .HaveCount(1, "the recovery tick sends RequestAdded and the director starts the head");
        PlaybackState state = harness.Rig.Registry.StateOf(RecoveryChannel);
        state.Kind.Should().Be(PlaybackStateKind.Starting);
        state.Item!.Uri.Should().Be("spotify:track:r1");
    }

    [Fact]
    public async Task A_second_poll_tick_while_the_director_is_Starting_starts_nothing_more()
    {
        Harness harness = Harness.WithWaitingHead();
        await harness.Sut.PollAllChannelsOnceAsync(CancellationToken.None);
        harness.Clock.Advance(TimeSpan.FromMinutes(2));

        await harness.Sut.PollAllChannelsOnceAsync(CancellationToken.None);

        harness.Spotify.Calls.Where(c => c.Name == "Start").Should().HaveCount(1);
        harness.Rig.Registry.StateOf(RecoveryChannel).Kind.Should().Be(PlaybackStateKind.Starting);
    }

    private sealed class Harness
    {
        private Harness(
            MusicStatePollingService sut,
            DirectorRig rig,
            FakeEngine spotify,
            FakeTimeProvider clock,
            IMusicService music
        )
        {
            Music = music;
            Sut = sut;
            Rig = rig;
            Spotify = spotify;
            Clock = clock;
        }

        public IMusicService Music { get; }

        public MusicStatePollingService Sut { get; }

        public DirectorRig Rig { get; }

        public FakeEngine Spotify { get; }

        public FakeTimeProvider Clock { get; }

        /// <summary>A connected channel whose director is Idle while one request waits at the head of its queue.</summary>
        public static Harness WithWaitingHead()
        {
            MusicTestDbContext db = MusicTestDbContext.New();
            db.Services.Add(
                new()
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "spotify",
                    BroadcasterId = RecoveryChannel,
                    Enabled = true,
                    AccessToken = "test-access-token",
                }
            );
            db.IntegrationConnections.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    BroadcasterId = RecoveryChannel,
                    Provider = "spotify",
                    Status = AuthEnums.IntegrationStatus.Connected,
                    ProviderAccountId = $"spotify-{RecoveryChannel:N}",
                }
            );
            db.SaveChanges();

            SongRequestQueueStore store = new();
            store
                .GetOrCreate(RecoveryChannel.ToString())
                .Enqueue(
                    "viewer-r1",
                    new(
                        "spotify:track:r1",
                        "Song r1",
                        "Artist",
                        null,
                        200_000,
                        "viewer-r1",
                        0,
                        null,
                        ""
                    )
                );
            DirectorRig rig = DirectorRig.Of(store);
            FakeEngine spotify = rig.Engines.EngineOf(RecoveryChannel, EngineKind.Spotify);

            IMusicProvider provider = Substitute.For<IMusicProvider>();
            provider.Provider.Returns("spotify");
            IMusicService music = Substitute.For<IMusicService>();
            FakeTimeProvider clock = new(new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            ServiceCollection services = new();
            services.AddSingleton<IApplicationDbContext>(db);
            services.AddSingleton(music);
            services.AddSingleton<ISongRequestQueueStore>(store);
            services.AddSingleton<IPlaybackInputSink>(rig.Registry);
            services.AddSingleton(provider);

            MusicStatePollingService sut = new(
                services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                new RecordingEventBus(),
                clock,
                Substitute.For<IChannelRegistry>(),
                Substitute.For<IEngineFeed>(),
                NullLogger<MusicStatePollingService>.Instance
            );

            return new(sut, rig, spotify, clock, music);
        }
    }
}
