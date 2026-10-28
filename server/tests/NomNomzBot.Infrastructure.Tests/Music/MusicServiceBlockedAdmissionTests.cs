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

using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Services;
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
            new("spotify", "spotify:track:q1", "Song Q", "banned on stream")
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
        await blocks.BlockAsync(ChannelA, new("spotify", "spotify:track:q1", "Song Q"));

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
        await blocks.BlockAsync(ChannelA, new("spotify", "spotify:track:q1", "Song Q"));

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
        MusicTestDbContext db = MusicTestDbContext.New();
        // Routing seed: MusicService.GetActiveProviderAsync selects the active provider by which
        // Service names are connected — a separate concern from SpotifyMusicProvider's OWN token
        // resolution (which reads the vault below, S003).
        foreach (Guid channel in connectedChannels)
        {
            db.Services.Add(
                new()
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

        FakeIntegrationTokenVault vault = new(db);
        foreach (Guid channel in connectedChannels)
            vault.SeedConnectedSpotify(channel);

        SpotifyMusicProvider spotify = new(
            db,
            vault,
            new InMemoryIntegrationCapabilityStore(),
            new LastActiveSpotifyDeviceTracker(),
            new SpotifyRateLimitCooldowns(new NullActionRequiredChangeNotifier()),
            new SingleHandlerClientFactory(new SearchFakeSpotifyHandler()),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance,
            NullSystemCredentialsProvider.Instance,
            new ConnectionRefreshGate(),
            new NullChannelCredentialsResolver(NullSystemCredentialsProvider.Instance),
            new OutboundSanctionAccessor()
        );

        RecordingEventBus bus = new();
        BlockedTrackService blocks = new(db);
        MusicService sut = new(
            [spotify],
            db,
            bus,
            blocks,
            DirectorRig.NewStore(out SongRequestQueueStore directorStore),
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            PermissiveMusicConfigService.Instance,
            Substitute.For<ICurrencyAccountService>(),
            new NowPlayingCache(),
            new OutboundSanctionAccessor(),
            Substitute.For<IUserIdentityService>(),
            Substitute.For<IForeignLinkTitleLookup>(),
            DirectorRig.Of(directorStore).Registry,
            DirectorRig.Of(directorStore).Registry
        );
        return (sut, bus, blocks);
    }

    /// <summary>Search always resolves to the one canned track "Song Q"; everything else is a 204.</summary>
    private sealed class SearchFakeSpotifyHandler : HttpMessageHandler
    {
        private const string SearchJson = """
            {"tracks":{"items":[{"name":"Song Q","uri":"spotify:track:q1","duration_ms":200000,"artists":[{"name":"Artist"}],"album":{"name":"Album","images":[]}}]}}
            """;

        private const string TrackJson = """
            {"id":"q1","name":"Song Q","uri":"spotify:track:q1","duration_ms":200000,"artists":[{"name":"Artist"}],"album":{"name":"Album","images":[]}}
            """;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            // A pasted spotify:track: URI is looked up by id, not searched as text.
            string? body =
                path.EndsWith("/search", StringComparison.Ordinal) ? SearchJson
                : path.EndsWith("/tracks/q1", StringComparison.Ordinal) ? TrackJson
                : null;

            HttpResponseMessage response = body is null
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : new(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
            return Task.FromResult(response);
        }
    }
}
