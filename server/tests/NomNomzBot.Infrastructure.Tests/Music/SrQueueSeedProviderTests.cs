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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Notifications;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves the sr_queue widget gets the current queue back on join: requests queued through the real
/// <see cref="MusicService"/> are replayed as the same <c>sr_queue</c> <c>{ items }</c> frame the live broadcast
/// sends, in queue order, and an empty queue still gives one frame with no items so the widget clears.
/// </summary>
public sealed class SrQueueSeedProviderTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f0003");

    [Fact]
    public async Task Seed_replays_the_queued_requests_in_order_as_the_live_sr_queue_frame()
    {
        MusicService music = BuildMusic();
        (await music.AddToQueueAsync(ChannelId.ToString(), "spotify:track:q1", "viewer1"))
            .IsSuccess.Should()
            .BeTrue();
        (await music.AddToQueueAsync(ChannelId.ToString(), "spotify:track:q2", "viewer2"))
            .IsSuccess.Should()
            .BeTrue();
        SrQueueSeedProvider sut = new(music);

        IReadOnlyList<WidgetSeedFrame> frames = await sut.SeedAsync(
            ChannelId,
            new Widget { BroadcasterId = ChannelId },
            CancellationToken.None
        );

        sut.NaturalKey.Should().Be("sr_queue");
        frames.Should().ContainSingle();
        frames[0].EventType.Should().Be("sr_queue");
        JObject wire = Wire(frames[0]);
        JArray items = (JArray)wire["items"]!;
        items.Should().HaveCount(2);
        items[0]["title"]!.Value<string>().Should().Be("Song One");
        items[0]["requestedBy"]!.Value<string>().Should().Be("viewer1");
        items[0]["durationSec"]!.Value<int>().Should().Be(200);
        items[0]["code"]!.Value<string>().Should().NotBeNullOrEmpty();
        items[1]["title"]!.Value<string>().Should().Be("Song Two");
        items[1]["requestedBy"]!.Value<string>().Should().Be("viewer2");
        items[1]["durationSec"]!.Value<int>().Should().Be(180);
        items[1]["code"]!.Value<string>().Should().NotBeNullOrEmpty();
        items[1]["code"]!.Value<string>().Should().NotBe(items[0]["code"]!.Value<string>());
    }

    [Fact]
    public async Task Seed_gives_one_frame_with_no_items_for_an_empty_queue()
    {
        SrQueueSeedProvider sut = new(BuildMusic());

        IReadOnlyList<WidgetSeedFrame> frames = await sut.SeedAsync(
            ChannelId,
            new Widget { BroadcasterId = ChannelId },
            CancellationToken.None
        );

        frames.Should().ContainSingle();
        frames[0].EventType.Should().Be("sr_queue");
        JObject wire = Wire(frames[0]);
        wire["items"].Should().BeOfType<JArray>().Which.Should().BeEmpty();
    }

    /// <summary>The frame data as the hub puts it on the wire: camelCase.</summary>
    private static JObject Wire(WidgetSeedFrame frame) =>
        JObject.Parse(
            JsonConvert.SerializeObject(
                frame.Data,
                new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver(),
                }
            )
        );

    private static MusicService BuildMusic()
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
            new SingleHandlerClientFactory(new TwoTrackSpotifyHandler()),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance,
            NullSystemCredentialsProvider.Instance,
            new ConnectionRefreshGate(),
            new NullChannelCredentialsResolver(NullSystemCredentialsProvider.Instance),
            new OutboundSanctionAccessor()
        );

        return new MusicService(
            [spotify],
            db,
            new RecordingEventBus(),
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
    }

    /// <summary>Search answers with Song Two when the query names q2, Song One otherwise; every other call is a 204.</summary>
    private sealed class TwoTrackSpotifyHandler : HttpMessageHandler
    {
        private const string SongOne =
            """{"tracks":{"items":[{"name":"Song One","uri":"spotify:track:q1","duration_ms":200000,"artists":[{"name":"Artist"}],"album":{"name":"Album","images":[]}}]}}""";
        private const string SongTwo =
            """{"tracks":{"items":[{"name":"Song Two","uri":"spotify:track:q2","duration_ms":180000,"artists":[{"name":"Artist"}],"album":{"name":"Album","images":[]}}]}}""";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Uri uri = request.RequestUri!;
            if (!uri.AbsolutePath.EndsWith("/search", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));

            string json = uri.Query.Contains("q2", StringComparison.Ordinal) ? SongTwo : SongOne;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
