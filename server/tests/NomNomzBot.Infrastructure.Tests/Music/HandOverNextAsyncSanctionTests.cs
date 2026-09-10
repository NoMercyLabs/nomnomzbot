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
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// <see cref="MusicService.HandOverNextAsync"/> runs with no person present — off the reconciler's
/// playback-changed reaction or the poller's recovery tick — so unlike an admission made inside a live
/// HTTP request (which already carries a sanction from <c>OutboundSanctionFilter</c>) it has no ambient
/// <see cref="OutboundSanction"/> of its own to inherit. Live 2026-09-10: every advance past the first
/// admitted request was silently refused by <c>OutboundSanctionHandler</c> — a background write with
/// nothing recorded who authorised it — so the song-request queue accepted requests but never dispatched
/// past the very first one. These tests pin that <see cref="MusicService.HandOverNextAsync"/> establishes
/// its own basis before touching the provider, using a real <see cref="OutboundSanctionAccessor"/> and a
/// provider double that reads <see cref="IOutboundSanctionAccessor.Current"/> at the moment it is called
/// — a regression here means the queue is dispatching WITH NOTHING actually claiming to have authorised
/// it, which is exactly the silent-write failure mode this whole mechanism exists to catch
/// (OutboundSanctionHandler's own remarks).
/// </summary>
public sealed class HandOverNextAsyncSanctionTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-00000000ab01");

    private static void SeedConnectedSpotify(MusicTestDbContext db) =>
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

    private static SongRequestEntry PendingEntry(string trackUri, string requestedBy) =>
        new(trackUri, "Track", "Artist", null, 200_000, requestedBy, 0, null, "");

    [Fact]
    public async Task HandOverNextAsync_establishes_a_channel_configuration_sanction_before_pushing()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        SeedConnectedSpotify(db);
        db.SaveChanges();

        OutboundSanctionAccessor sanctions = new();
        OutboundSanction? observedDuringPush = null;
        IMusicProvider provider = Substitute.For<IMusicProvider>();
        provider.Provider.Returns("spotify");
        provider
            .AddToQueueAsync(ChannelId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                observedDuringPush = sanctions.Current;
                return Task.FromResult(true);
            });

        SongRequestQueueStore queueStore = new();
        queueStore
            .GetOrCreate(ChannelId.ToString())
            .Enqueue("viewer1", PendingEntry("uri:1", "viewer1"));

        MusicService sut = new(
            [provider],
            db,
            new RecordingEventBus(),
            new BlockedTrackService(db),
            queueStore,
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            PermissiveMusicConfigService.Instance,
            Substitute.For<ICurrencyAccountService>(),
            new NowPlayingCache(),
            sanctions
        );

        // No sanction in force here, matching exactly how the reconciler and the poller's recovery tick
        // call this — no live HTTP request, no person present.
        sanctions.Current.Should().BeNull();

        await sut.HandOverNextAsync(ChannelId.ToString());

        observedDuringPush
            .Should()
            .NotBeNull("the push must carry a sanction the write can be traced to");
        observedDuringPush!.Basis.Should().Be(OutboundSanctionBasis.ChannelConfiguration);
        observedDuringPush.Detail.Should().Be("music:song_request_dispatch");

        // The sanction is scoped to the push alone — it must not leak into whatever runs after.
        sanctions.Current.Should().BeNull();
        queueStore
            .GetInFlight(ChannelId.ToString())
            .Should()
            .NotBeNull("a successful push puts exactly one entry in flight");
    }

    [Fact]
    public async Task RemoveFromQueueAsync_on_the_in_flight_entry_clears_in_flight_so_the_next_request_can_dispatch()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        SeedConnectedSpotify(db);
        db.SaveChanges();

        int pushCount = 0;
        IMusicProvider provider = Substitute.For<IMusicProvider>();
        provider.Provider.Returns("spotify");
        provider
            .AddToQueueAsync(ChannelId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                pushCount++;
                return Task.FromResult(true);
            });

        SongRequestQueueStore queueStore = new();
        MusicService sut = new(
            [provider],
            db,
            new RecordingEventBus(),
            new BlockedTrackService(db),
            queueStore,
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            PermissiveMusicConfigService.Instance,
            Substitute.For<ICurrencyAccountService>(),
            new NowPlayingCache(),
            new OutboundSanctionAccessor()
        );

        queueStore
            .GetOrCreate(ChannelId.ToString())
            .Enqueue("viewer1", PendingEntry("uri:1", "viewer1"));
        queueStore
            .GetOrCreate(ChannelId.ToString())
            .Enqueue("viewer2", PendingEntry("uri:2", "viewer2"));

        await sut.HandOverNextAsync(ChannelId.ToString());
        pushCount.Should().Be(1, "only the head of the queue is ever pushed to the provider");
        queueStore.GetInFlight(ChannelId.ToString()).Should().NotBeNull();

        // A moderator removes the stuck head (position 0) — the one the provider already has.
        (await sut.RemoveFromQueueAsync(ChannelId.ToString(), 0))
            .Should()
            .BeTrue();

        queueStore
            .GetInFlight(ChannelId.ToString())
            .Should()
            .BeNull("nothing is actually at the provider once its queue entry is gone");

        await sut.HandOverNextAsync(ChannelId.ToString());

        pushCount
            .Should()
            .Be(
                2,
                "the second request must now reach the provider, not stay wedged behind a removed entry"
            );
    }
}
