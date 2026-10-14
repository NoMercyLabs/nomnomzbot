// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Notifications;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// S067a — proves <see cref="MusicService.PromoteToTopAsync"/> actually reorders the live fair queue
/// (not just returns true) and <see cref="MusicService.BanQueuedTrackAsync"/> actually calls
/// <see cref="IBlockedTrackService.BlockAsync"/> with the target queued track (not "now playing") and
/// removes it from the queue.
/// </summary>
public sealed class MusicServiceQueuePromoteBanTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f0003");

    [Fact]
    public async Task PromoteToTop_moves_the_targeted_entry_to_the_front_of_the_real_queue()
    {
        (MusicService sut, RecordingEventBus bus, MusicTestDbContext _) = Build();
        await sut.AddToQueueAsync(ChannelId.ToString(), "spotify:track:q1", "viewer1");
        await sut.AddToQueueAsync(ChannelId.ToString(), "spotify:track:q2", "viewer2");

        bool moved = await sut.PromoteToTopAsync(ChannelId.ToString(), 1);

        moved.Should().BeTrue();
        MusicQueue queue = await sut.GetQueueAsync(ChannelId.ToString());
        queue.Queue.Should().HaveCount(2);
        queue.Queue[0].RequestedBy.Should().Be("viewer2");
        queue.Queue[1].RequestedBy.Should().Be("viewer1");

        SongRequestQueueChangedEvent last = bus
            .Published.OfType<SongRequestQueueChangedEvent>()
            .Last();
        last.Items.Should().HaveCount(2);
        last.Items[0].RequestedBy.Should().Be("viewer2");
    }

    [Fact]
    public async Task PromoteToTop_out_of_range_position_returns_false_and_leaves_the_queue_untouched()
    {
        (MusicService sut, RecordingEventBus bus, MusicTestDbContext _) = Build();
        await sut.AddToQueueAsync(ChannelId.ToString(), "spotify:track:q1", "viewer1");

        bool moved = await sut.PromoteToTopAsync(ChannelId.ToString(), 5);

        moved.Should().BeFalse();
        MusicQueue queue = await sut.GetQueueAsync(ChannelId.ToString());
        queue.Queue.Should().ContainSingle(i => i.RequestedBy == "viewer1");
        // Only the enqueue publish fired — no spurious change event for a no-op.
        bus.Published.OfType<SongRequestQueueChangedEvent>().Should().HaveCount(1);
    }

    [Fact]
    public async Task BanQueuedTrack_blocks_the_targeted_track_not_whatever_is_playing()
    {
        (MusicService sut, RecordingEventBus bus, MusicTestDbContext db) = Build();
        await sut.AddToQueueAsync(ChannelId.ToString(), "spotify:track:q1", "viewer1");
        await sut.AddToQueueAsync(ChannelId.ToString(), "spotify:track:q2", "viewer2");

        Result<BlockedTrackDto> result = await sut.BanQueuedTrackAsync(
            ChannelId.ToString(),
            1, // "Song Q2" at position 1 — never played, distinct from any "now playing" track
            "moderator-1"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.TrackUri.Should().Be("spotify:track:q2");
        result.Value.BlockedByUserId.Should().Be("moderator-1");

        db.BlockedTracks.Should().ContainSingle(t => t.TrackUri == "spotify:track:q2");

        // Banned entries are pulled from the live queue too.
        MusicQueue queue = await sut.GetQueueAsync(ChannelId.ToString());
        queue.Queue.Should().ContainSingle(i => i.RequestedBy == "viewer1");

        SongRequestQueueChangedEvent last = bus
            .Published.OfType<SongRequestQueueChangedEvent>()
            .Last();
        last.Items.Should().ContainSingle(i => i.RequestedBy == "viewer1");
    }

    [Fact]
    public async Task BanQueuedTrack_out_of_range_position_fails_without_touching_the_blocklist()
    {
        (MusicService sut, _, MusicTestDbContext db) = Build();
        await sut.AddToQueueAsync(ChannelId.ToString(), "spotify:track:q1", "viewer1");

        Result<BlockedTrackDto> result = await sut.BanQueuedTrackAsync(ChannelId.ToString(), 3);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
        db.BlockedTracks.Should().BeEmpty();
    }

    private static async Task<(MusicService Sut, RecordingEventBus Bus)> BuildWithThreeQueued()
    {
        (MusicService sut, RecordingEventBus bus, MusicTestDbContext _) = Build();
        await sut.AddToQueueAsync(ChannelId.ToString(), "spotify:track:q1", "viewer1");
        await sut.AddToQueueAsync(ChannelId.ToString(), "spotify:track:q2", "viewer2");
        await sut.AddToQueueAsync(ChannelId.ToString(), "spotify:track:q3", "viewer3");
        return (sut, bus);
    }

    private static async Task<List<string?>> QueueOrder(MusicService sut)
    {
        MusicQueue queue = await sut.GetQueueAsync(ChannelId.ToString());
        return [.. queue.Queue.Select(i => i.RequestedBy)];
    }

    [Fact]
    public async Task Move_down_to_the_end_places_the_item_last_and_publishes_the_new_order()
    {
        (MusicService sut, RecordingEventBus bus) = await BuildWithThreeQueued();

        Result result = await sut.MoveAsync(ChannelId.ToString(), 0, 2);

        result.IsSuccess.Should().BeTrue();
        (await QueueOrder(sut)).Should().Equal("viewer2", "viewer3", "viewer1");
        SongRequestQueueChangedEvent last = bus
            .Published.OfType<SongRequestQueueChangedEvent>()
            .Last();
        last.Items.Select(i => i.RequestedBy).Should().Equal("viewer2", "viewer3", "viewer1");
    }

    [Fact]
    public async Task Move_up_places_the_item_exactly_at_the_dropped_position()
    {
        (MusicService sut, RecordingEventBus bus) = await BuildWithThreeQueued();

        Result result = await sut.MoveAsync(ChannelId.ToString(), 2, 1);

        result.IsSuccess.Should().BeTrue();
        (await QueueOrder(sut)).Should().Equal("viewer1", "viewer3", "viewer2");
        bus.Published.OfType<SongRequestQueueChangedEvent>()
            .Last()
            .Items.Select(i => i.RequestedBy)
            .Should()
            .Equal("viewer1", "viewer3", "viewer2");
    }

    [Fact]
    public async Task Move_to_the_same_position_succeeds_without_changing_anything()
    {
        (MusicService sut, RecordingEventBus bus) = await BuildWithThreeQueued();
        int publishedBefore = bus.Published.OfType<SongRequestQueueChangedEvent>().Count();

        Result result = await sut.MoveAsync(ChannelId.ToString(), 1, 1);

        result.IsSuccess.Should().BeTrue();
        (await QueueOrder(sut)).Should().Equal("viewer1", "viewer2", "viewer3");
        bus.Published.OfType<SongRequestQueueChangedEvent>().Should().HaveCount(publishedBefore);
    }

    [Fact]
    public async Task Move_from_out_of_range_is_not_found_and_leaves_the_queue_untouched()
    {
        (MusicService sut, RecordingEventBus bus) = await BuildWithThreeQueued();
        int publishedBefore = bus.Published.OfType<SongRequestQueueChangedEvent>().Count();

        Result result = await sut.MoveAsync(ChannelId.ToString(), 3, 0);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
        (await QueueOrder(sut)).Should().Equal("viewer1", "viewer2", "viewer3");
        bus.Published.OfType<SongRequestQueueChangedEvent>().Should().HaveCount(publishedBefore);
    }

    [Fact]
    public async Task Move_to_out_of_range_fails_validation_and_leaves_the_queue_untouched()
    {
        (MusicService sut, RecordingEventBus bus) = await BuildWithThreeQueued();
        int publishedBefore = bus.Published.OfType<SongRequestQueueChangedEvent>().Count();

        Result result = await sut.MoveAsync(ChannelId.ToString(), 0, 3);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await QueueOrder(sut)).Should().Equal("viewer1", "viewer2", "viewer3");
        bus.Published.OfType<SongRequestQueueChangedEvent>().Should().HaveCount(publishedBefore);
    }

    [Fact]
    public async Task Move_keeps_fair_ranking_for_a_later_request_from_the_moved_owner()
    {
        (MusicService sut, _) = await BuildWithThreeQueued();
        await sut.MoveAsync(ChannelId.ToString(), 0, 2);

        // viewer1's item now sits last with rank 1; their next request is rank 2 and lands after it.
        await sut.AddToQueueAsync(ChannelId.ToString(), "spotify:track:q4", "viewer1");

        (await QueueOrder(sut)).Should().Equal("viewer2", "viewer3", "viewer1", "viewer1");
    }

    private static (MusicService Sut, RecordingEventBus Bus, MusicTestDbContext Db) Build()
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

        FakeIntegrationTokenVault vault = new(db);
        vault.SeedConnectedSpotify(ChannelId);

        SpotifyMusicProvider spotify = new(
            db,
            vault,
            new InMemoryIntegrationCapabilityStore(),
            new LastActiveSpotifyDeviceTracker(),
            new SpotifyRateLimitCooldowns(new NullActionRequiredChangeNotifier()),
            new SingleHandlerClientFactory(new QueueFakeSpotifyHandler()),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance,
            NullSystemCredentialsProvider.Instance,
            new ConnectionRefreshGate(),
            new NullChannelCredentialsResolver(NullSystemCredentialsProvider.Instance),
            new OutboundSanctionAccessor()
        );

        RecordingEventBus bus = new();
        MusicService sut = new(
            [spotify],
            db,
            bus,
            new BlockedTrackService(db),
            new SongRequestQueueStore(),
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
        return (sut, bus, db);
    }

    /// <summary>Stubs the Spotify surface these flows touch: search resolves either of two distinct
    /// tracks by URI, currently-playing returns "nothing playing" (204), every mutation returns 204.</summary>
    private sealed class QueueFakeSpotifyHandler : HttpMessageHandler
    {
        private const string SearchJsonQ1 = """
            {"tracks":{"items":[{"name":"Song Q1","uri":"spotify:track:q1","duration_ms":200000,"artists":[{"name":"Artist"}],"album":{"name":"Album","images":[]}}]}}
            """;
        private const string SearchJsonQ2 = """
            {"tracks":{"items":[{"name":"Song Q2","uri":"spotify:track:q2","duration_ms":210000,"artists":[{"name":"Artist"}],"album":{"name":"Album","images":[]}}]}}
            """;

        private const string SearchJsonQ3 = """
            {"tracks":{"items":[{"name":"Song Q3","uri":"spotify:track:q3","duration_ms":220000,"artists":[{"name":"Artist"}],"album":{"name":"Album","images":[]}}]}}
            """;
        private const string SearchJsonQ4 = """
            {"tracks":{"items":[{"name":"Song Q4","uri":"spotify:track:q4","duration_ms":230000,"artists":[{"name":"Artist"}],"album":{"name":"Album","images":[]}}]}}
            """;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            bool isSearch = request.RequestUri!.AbsolutePath.EndsWith(
                "/search",
                StringComparison.Ordinal
            );

            if (!isSearch)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));

            string query = request.RequestUri!.Query;
            string json =
                query.Contains("q1", StringComparison.OrdinalIgnoreCase) ? SearchJsonQ1
                : query.Contains("q3", StringComparison.OrdinalIgnoreCase) ? SearchJsonQ3
                : query.Contains("q4", StringComparison.OrdinalIgnoreCase) ? SearchJsonQ4
                : SearchJsonQ2;

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
