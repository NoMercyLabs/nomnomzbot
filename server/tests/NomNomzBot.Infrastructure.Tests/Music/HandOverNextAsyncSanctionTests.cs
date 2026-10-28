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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// A song request is admitted with no person present when a chat command (`!sr`) reaches the service, so
/// unlike an admission made inside a live HTTP request (which already carries a sanction from
/// <c>OutboundSanctionFilter</c>) it has no ambient <see cref="OutboundSanction"/> of its own to inherit.
/// Live 2026-09-10: every start past the first admitted request was silently refused by
/// <c>OutboundSanctionHandler</c> — a background write with nothing recorded who authorised it. These tests
/// pin that the admission establishes its own basis before the director's engine start, read through a real
/// <see cref="OutboundSanctionAccessor"/> at the moment the engine is called — a regression here means the
/// queue starts songs WITH NOTHING actually claiming to have authorised it, which is exactly the
/// silent-write failure mode this whole mechanism exists to catch (OutboundSanctionHandler's own remarks).
/// It also pins that removing the request the director started frees the in-flight slot.
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

    /// <summary>
    /// A provider that resolves no metadata, so admission degrades to a placeholder entry carrying the
    /// requested uri (its caller already trusts the uri as real) and never pushes to a provider queue.
    /// </summary>
    private static IMusicProvider NewProvider()
    {
        IMusicProvider provider = Substitute.For<IMusicProvider>();
        provider.Provider.Returns("spotify");
        provider
            .ResolveTrackAsync(ChannelId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(((TrackInfo?)null, MusicProviderFailureReason.None));
        provider
            .SearchAsync(ChannelId, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((Array.Empty<TrackInfo>(), MusicProviderFailureReason.None));
        return provider;
    }

    private static MusicService NewService(
        IMusicProvider provider,
        SongRequestQueueStore store,
        IOutboundSanctionAccessor sanctions
    )
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        SeedConnectedSpotify(db);
        db.SaveChanges();

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
            sanctions,
            Substitute.For<IUserIdentityService>(),
            Substitute.For<IForeignLinkTitleLookup>(),
            DirectorRig.Of(store).Registry,
            DirectorRig.Of(store).Registry
        );
    }

    /// <summary>
    /// Admission's own immediate start (the first request in an otherwise-empty queue, reached from a
    /// chat-triggered `!sr` with no live HTTP request of its own) has the "no person present" shape and
    /// was refused by the sanction guard (live 2026-09-10).
    /// </summary>
    [Fact]
    public async Task AddToQueueAsync_admission_start_establishes_a_channel_configuration_sanction()
    {
        OutboundSanctionAccessor sanctions = new();
        OutboundSanction? observedDuringStart = null;
        IMusicProvider provider = NewProvider();
        DirectorRig.NewStore(out SongRequestQueueStore store);
        // The director's engine start is the write that must be traceable: the fake engine reads the ambient
        // sanction at the moment the director asks it to start the track.
        FakeEngine spotify = DirectorRig.Of(store).Engines.EngineOf(ChannelId, EngineKind.Spotify);
        spotify.DuringStart = _ =>
        {
            observedDuringStart = sanctions.Current;
            return Task.CompletedTask;
        };
        MusicService sut = NewService(provider, store, sanctions);

        sanctions.Current.Should().BeNull();

        Result admitted = await sut.AddToQueueAsync(
            ChannelId.ToString(),
            "spotify:track:sanction1",
            "viewer1"
        );

        admitted.IsSuccess.Should().BeTrue();
        spotify.Calls.Where(c => c.Name == "Start").Should().HaveCount(1);
        observedDuringStart
            .Should()
            .NotBeNull("the engine start must carry a sanction the write can be traced to");
        observedDuringStart!.Basis.Should().Be(OutboundSanctionBasis.ChannelConfiguration);
        observedDuringStart.Detail.Should().Be("music:song_request_dispatch");
        // The sanction is scoped to the start alone — it must not leak into whatever runs after.
        sanctions.Current.Should().BeNull();
        store.GetInFlight(ChannelId.ToString())!.TrackUri.Should().Be("spotify:track:sanction1");
        await provider
            .DidNotReceive()
            .AddToQueueAsync(ChannelId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveFromQueueAsync_on_the_in_flight_entry_clears_in_flight_and_arms_the_skip_marker()
    {
        IMusicProvider provider = NewProvider();
        DirectorRig.NewStore(out SongRequestQueueStore store);
        MusicService sut = NewService(provider, store, new OutboundSanctionAccessor());

        (await sut.AddToQueueAsync(ChannelId.ToString(), "spotify:track:remove1", "viewer1"))
            .IsSuccess.Should()
            .BeTrue();
        (await sut.AddToQueueAsync(ChannelId.ToString(), "spotify:track:remove2", "viewer2"))
            .IsSuccess.Should()
            .BeTrue();
        store
            .GetInFlight(ChannelId.ToString())!
            .TrackUri.Should()
            .Be("spotify:track:remove1", "only the head is ever started; the second waits");

        // A moderator removes the stuck head (position 0) — the one the director already started.
        (await sut.RemoveFromQueueAsync(ChannelId.ToString(), 0))
            .Should()
            .BeTrue();

        store
            .GetInFlight(ChannelId.ToString())
            .Should()
            .BeNull("nothing is in flight once its queue entry is gone");
        store
            .GetOrCreate(ChannelId.ToString())
            .GetSnapshot()
            .Select(e => e.Item.TrackUri)
            .Should()
            .Equal(["spotify:track:remove2"], "the second request still waits in the queue");
        store
            .TryConsumeRetraction(ChannelId.ToString(), "spotify:track:remove1")
            .Should()
            .BeTrue(
                "the engine may still hold the removed track, so its start is marked to be skipped"
            );
        store
            .TryConsumeRetraction(ChannelId.ToString(), "spotify:track:remove2")
            .Should()
            .BeFalse("a request that was never started is not marked");
    }
}
