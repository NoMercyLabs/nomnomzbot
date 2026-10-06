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
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

// Live 2026-10-01/02: a channel's own Spotify app was blocked for hours, twice. The poller already reads the
// player every second while the channel is watched, yet every Stream Deck key, overlay and dashboard read sent
// its own extra call on top — 22 of them in the 40 seconds before the first block.
public sealed class NowPlayingReaderCacheTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-00000000ac77");

    private sealed class Harness
    {
        public required MusicService Sut { get; init; }
        public required NowPlayingCache Cache { get; init; }
        public required IMusicProvider Provider { get; init; }
        public int ProviderReads { get; set; }
        public string Channel => ChannelId.ToString();
    }

    private static Harness Build()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        db.Services.Add(
            new()
            {
                Id = Guid.NewGuid().ToString(),
                Name = "spotify",
                BroadcasterId = ChannelId,
                Enabled = true,
                AccessToken = "test-access-token",
            }
        );
        db.SaveChanges();

        IMusicProvider provider = Substitute.For<IMusicProvider>();
        NowPlayingCache cache = new();
        Harness harness = new()
        {
            Sut = new(
                [provider],
                db,
                new RecordingEventBus(),
                new BlockedTrackService(db),
                new SongRequestQueueStore(),
                new NoOpSongRequestQueuePersistence(),
                NullLogger<MusicService>.Instance,
                new InMemoryIntegrationCapabilityStore(),
                PermissiveMusicConfigService.Instance,
                Substitute.For<ICurrencyAccountService>(),
                cache,
                new OutboundSanctionAccessor(),
                Substitute.For<IUserIdentityService>(),
                Substitute.For<IForeignLinkTitleLookup>()
            ),
            Cache = cache,
            Provider = provider,
        };

        provider.Provider.Returns("spotify");
        provider.Capabilities.Returns(MusicProviderCapabilities.NowPlaying);
        provider
            .GetCurrentTrackAsync(ChannelId, Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(_ =>
            {
                harness.ProviderReads++;
                return Task.FromResult<TrackInfo?>(Track("From Spotify", progressMs: 10_000));
            });
        return harness;
    }

    private static TrackInfo Track(string name, int progressMs) =>
        new()
        {
            TrackName = name,
            Artist = "Artist",
            Album = "Album",
            TrackUri = "spotify:track:" + name,
            Provider = "spotify",
            DurationMs = 200_000,
            ProgressMs = progressMs,
            IsPlaying = true,
        };

    [Fact]
    public async Task A_burst_of_readers_costs_one_spotify_call()
    {
        Harness h = Build();

        for (int i = 0; i < 22; i++)
            await h.Sut.GetNowPlayingAsync(h.Channel);

        h.ProviderReads.Should().Be(1);
    }

    [Fact]
    public async Task A_reader_gets_the_poller_answer_with_progress_moved_on_to_now()
    {
        Harness h = Build();
        h.Cache.Set(
            ChannelId,
            Track("Polled", progressMs: 60_000),
            DateTimeOffset.UtcNow.AddMilliseconds(-500)
        );

        NowPlaying? now = await h.Sut.GetNowPlayingAsync(h.Channel);

        h.ProviderReads.Should().Be(0);
        now!.TrackName.Should().Be("Polled");
        now.ProgressMs.Should().BeInRange(60_500, 61_500);
    }

    [Fact]
    public async Task An_answer_older_than_one_second_is_read_again()
    {
        Harness h = Build();
        h.Cache.Set(
            ChannelId,
            Track("Old", progressMs: 60_000),
            DateTimeOffset.UtcNow.AddSeconds(-2)
        );

        NowPlaying? now = await h.Sut.GetNowPlayingAsync(h.Channel);

        h.ProviderReads.Should().Be(1);
        now!.TrackName.Should().Be("From Spotify");
    }

    [Fact]
    public async Task The_poller_always_asks_spotify_itself()
    {
        Harness h = Build();
        h.Cache.Set(ChannelId, Track("Polled", progressMs: 60_000), DateTimeOffset.UtcNow);

        NowPlaying? now = await h.Sut.GetNowPlayingAsync(h.Channel, isBackgroundPoll: true);

        h.ProviderReads.Should().Be(1);
        now!.TrackName.Should().Be("From Spotify");
    }
}
