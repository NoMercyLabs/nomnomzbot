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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves the blocklist admission gate inside <see cref="MusicService.AddToQueueAsync"/> — the ONE
/// admission path every SR flow (the <c>!sr</c> builtin, the reward-pipeline <c>song_request</c>
/// action, the public SR page, and scripts) funnels through: a blocked track is refused with the
/// typed <c>TRACK_BLOCKED</c> reason and the fair queue stays untouched (no queue-changed event, no
/// accepted-request fact); a non-blocked track still queues; and the block is tenant-scoped —
/// channel B queues the same track freely.
/// </summary>
public sealed class MusicServiceBlockedAdmissionTests
{
    private static readonly Guid ChannelA = Guid.Parse("0192a000-0000-7000-8000-0000000ad001");
    private static readonly Guid ChannelB = Guid.Parse("0192a000-0000-7000-8000-0000000ad002");

    [Fact]
    public async Task A_blocked_track_is_refused_with_the_typed_reason_and_never_queued()
    {
        (MusicService sut, RecordingEventBus bus, BlockedTrackService blocks) = Build(
            ChannelA,
            ChannelB
        );
        await blocks.BlockAsync(
            ChannelA,
            new BlockTrackRequest("spotify", "spotify:track:q1", "Song Q", "banned on stream")
        );

        Result admitted = await sut.AddToQueueAsync(
            ChannelA.ToString(),
            "spotify:track:q1",
            "viewer1"
        );

        admitted.ErrorCode.Should().Be("TRACK_BLOCKED");
        admitted.ErrorMessage.Should().Be("\"Song Q\" is blocked in this channel.");

        // The refusal happened BEFORE queueing: no snapshot event, no accepted-request fact, empty queue.
        bus.Published.OfType<SongRequestQueueChangedEvent>().Should().BeEmpty();
        bus.Published.OfType<SongRequestedEvent>().Should().BeEmpty();
        MusicQueue queue = await sut.GetQueueAsync(ChannelA.ToString());
        queue.Queue.Should().BeEmpty();
    }

    [Fact]
    public async Task A_non_blocked_track_still_queues()
    {
        (MusicService sut, RecordingEventBus bus, _) = Build(ChannelA, ChannelB);

        Result admitted = await sut.AddToQueueAsync(
            ChannelA.ToString(),
            "spotify:track:q1",
            "viewer1"
        );

        admitted.IsSuccess.Should().BeTrue();
        bus.Published.OfType<SongRequestedEvent>()
            .Single()
            .TrackUri.Should()
            .Be("spotify:track:q1");
        (await sut.GetQueueAsync(ChannelA.ToString())).Queue.Should().ContainSingle();
    }

    [Fact]
    public async Task The_block_is_tenant_scoped_channel_B_queues_the_same_track()
    {
        (MusicService sut, _, BlockedTrackService blocks) = Build(ChannelA, ChannelB);
        await blocks.BlockAsync(
            ChannelA,
            new BlockTrackRequest("spotify", "spotify:track:q1", "Song Q")
        );

        Result admittedB = await sut.AddToQueueAsync(
            ChannelB.ToString(),
            "spotify:track:q1",
            "viewer2"
        );

        admittedB.IsSuccess.Should().BeTrue();
        (await sut.GetQueueAsync(ChannelB.ToString())).Queue.Should().ContainSingle();
    }

    [Fact]
    public async Task A_request_by_raw_query_resolves_to_the_track_and_is_still_refused()
    {
        // The public SR page and dashboard submit raw queries, not URIs — the gate must hold there too.
        (MusicService sut, _, BlockedTrackService blocks) = Build(ChannelA, ChannelB);
        await blocks.BlockAsync(
            ChannelA,
            new BlockTrackRequest("spotify", "spotify:track:q1", "Song Q")
        );

        Result admitted = await sut.AddToQueueAsync(
            ChannelA.ToString(),
            "song q please",
            "viewer1"
        );

        admitted.ErrorCode.Should().Be("TRACK_BLOCKED");
        (await sut.GetQueueAsync(ChannelA.ToString())).Queue.Should().BeEmpty();
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

    private static (MusicService Sut, RecordingEventBus Bus, BlockedTrackService Blocks) Build(
        params Guid[] connectedChannels
    )
    {
        MusicTestDbContext db = new(
            new DbContextOptionsBuilder<MusicTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        foreach (Guid channel in connectedChannels)
        {
            db.Services.Add(
                new Service
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "spotify",
                    BroadcasterId = channel,
                    Enabled = true,
                    AccessToken = "test-access-token",
                }
            );
        }
        db.SaveChanges();

        SpotifyMusicProvider spotify = new(
            db,
            new PassthroughProtector(),
            new InMemoryIntegrationCapabilityStore(),
            new SingleHandlerClientFactory(new SearchFakeSpotifyHandler()),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance
        );

        RecordingEventBus bus = new();
        BlockedTrackService blocks = new(db);
        MusicService sut = new([spotify], db, bus, blocks, NullLogger<MusicService>.Instance);
        return (sut, bus, blocks);
    }

    /// <summary>Search always resolves to the one canned track "Song Q"; everything else is a 204.</summary>
    private sealed class SearchFakeSpotifyHandler : HttpMessageHandler
    {
        private const string SearchJson = """
            {"tracks":{"items":[{"name":"Song Q","uri":"spotify:track:q1","duration_ms":200000,"artists":[{"name":"Artist"}],"album":{"name":"Album","images":[]}}]}}
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

            HttpResponseMessage response = isSearch
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(SearchJson, Encoding.UTF8, "application/json"),
                }
                : new HttpResponseMessage(HttpStatusCode.NoContent);
            return Task.FromResult(response);
        }
    }
}
