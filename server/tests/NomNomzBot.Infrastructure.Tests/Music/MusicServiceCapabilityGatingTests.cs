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
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves <see cref="MusicService"/>'s §3.5 capability gating over the widened provider seam: a
/// transport call on a provider lacking the capability fails closed with
/// <c>CAPABILITY_UNSUPPORTED</c> (never a throw, never a provider API call), while Spotify
/// transport rides the widened Guid-keyed members — including the seam's whole-seconds seek
/// contract and the volume/previous wires observed on the actual HTTP. Uses the real providers
/// (YouTube over its Data API, Spotify over stubbed HTTP), not substitutes.
/// </summary>
public sealed class MusicServiceCapabilityGatingTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f4001");

    [Fact]
    public async Task Transport_members_fail_closed_on_a_provider_without_the_capability()
    {
        // YouTube-only channel: the YouTube Data API has no playback transport, so every
        // transport member gates off (§3.5) with CAPABILITY_UNSUPPORTED — no exception.
        (MusicService sut, RecordingHttpHandler handler) = Build(connectedService: "youtube");
        string channel = ChannelId.ToString();

        (await sut.PlayAsync(channel)).ErrorCode.Should().Be("CAPABILITY_UNSUPPORTED");
        (await sut.PauseAsync(channel)).ErrorCode.Should().Be("CAPABILITY_UNSUPPORTED");
        (await sut.SkipAsync(channel)).ErrorCode.Should().Be("CAPABILITY_UNSUPPORTED");
        (await sut.PreviousAsync(channel)).ErrorCode.Should().Be("CAPABILITY_UNSUPPORTED");
        (await sut.SeekAsync(channel, 5_000)).ErrorCode.Should().Be("CAPABILITY_UNSUPPORTED");
        (await sut.SetShuffleAsync(channel, true)).ErrorCode.Should().Be("CAPABILITY_UNSUPPORTED");
        (await sut.SetRepeatAsync(channel, "track"))
            .ErrorCode.Should()
            .Be("CAPABILITY_UNSUPPORTED");
        (await sut.SetVolumeAsync(channel, 50)).ErrorCode.Should().Be("CAPABILITY_UNSUPPORTED");
        (await sut.TransferPlaybackAsync(channel, "device-1"))
            .ErrorCode.Should()
            .Be("CAPABILITY_UNSUPPORTED");
        (await sut.GetDevicesAsync(channel)).Should().BeEmpty();
        (await sut.GetPlaylistsAsync(channel)).Should().BeEmpty();
        (await sut.PlayContextAsync(channel, "spotify:playlist:xyz")).Should().BeFalse();

        handler.RequestUrls.Should().BeEmpty("a gated member must never reach a provider API");
    }

    [Fact]
    public async Task Seek_crosses_the_seam_in_whole_seconds_and_reaches_Spotify_in_milliseconds()
    {
        (MusicService sut, RecordingHttpHandler handler) = Build(connectedService: "spotify");

        Result ok = await sut.SeekAsync(ChannelId.ToString(), 93_500);

        ok.IsSuccess.Should().BeTrue();
        handler
            .RequestUrls.Should()
            .ContainSingle(url => url.Contains("/me/player/seek?position_ms=93000"));
    }

    [Fact]
    public async Task Volume_reaches_Spotify_with_the_exact_volume_percent_query()
    {
        (MusicService sut, RecordingHttpHandler handler) = Build(connectedService: "spotify");

        Result ok = await sut.SetVolumeAsync(ChannelId.ToString(), 63);

        ok.IsSuccess.Should().BeTrue();
        handler
            .RequestUrls.Should()
            .ContainSingle(url =>
                url.StartsWith("PUT ") && url.Contains("/me/player/volume?volume_percent=63")
            );
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task Out_of_range_volume_fails_validation_without_an_API_call(int volume)
    {
        (MusicService sut, RecordingHttpHandler handler) = Build(connectedService: "spotify");

        Result result = await sut.SetVolumeAsync(ChannelId.ToString(), volume);

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        handler.RequestUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task Previous_reaches_Spotify_on_the_previous_endpoint()
    {
        (MusicService sut, RecordingHttpHandler handler) = Build(connectedService: "spotify");

        Result ok = await sut.PreviousAsync(ChannelId.ToString());

        ok.IsSuccess.Should().BeTrue();
        handler
            .RequestUrls.Should()
            .Contain(url => url.StartsWith("POST ") && url.EndsWith("/me/player/previous"));
    }

    [Fact]
    public async Task Negative_seek_fails_validation_without_an_API_call()
    {
        (MusicService sut, RecordingHttpHandler handler) = Build(connectedService: "spotify");

        Result result = await sut.SeekAsync(ChannelId.ToString(), -1);

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        handler.RequestUrls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("off")]
    [InlineData("track")]
    [InlineData("Context")]
    public async Task Repeat_mode_strings_map_onto_the_seam_enum_and_reach_Spotify(string mode)
    {
        (MusicService sut, RecordingHttpHandler handler) = Build(connectedService: "spotify");

        Result ok = await sut.SetRepeatAsync(ChannelId.ToString(), mode);

        ok.IsSuccess.Should().BeTrue();
        handler
            .RequestUrls.Should()
            .ContainSingle(url =>
                url.Contains($"/me/player/repeat?state={mode.ToLowerInvariant()}")
            );
    }

    [Fact]
    public async Task Invalid_repeat_mode_fails_validation_without_an_API_call()
    {
        (MusicService sut, RecordingHttpHandler handler) = Build(connectedService: "spotify");

        Result result = await sut.SetRepeatAsync(ChannelId.ToString(), "banana");

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        handler.RequestUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task Devices_map_field_by_field_from_the_provider_payload()
    {
        (MusicService sut, RecordingHttpHandler handler) = Build(connectedService: "spotify");
        handler.RespondWhen(
            r =>
                r.RequestUri!.AbsolutePath.EndsWith("/me/player/devices", StringComparison.Ordinal),
            HttpStatusCode.OK,
            """
            {"devices":[
              {"id":"dev1","name":"Streaming PC","type":"Computer","is_active":true,"volume_percent":63},
              {"id":"dev2","name":"Phone","type":"Smartphone","is_active":false,"volume_percent":null}
            ]}
            """
        );

        IReadOnlyList<MusicDeviceDto> devices = await sut.GetDevicesAsync(ChannelId.ToString());

        devices.Should().HaveCount(2);
        devices[0].Should().Be(new MusicDeviceDto("dev1", "Streaming PC", "Computer", true, 63));
        devices[1]
            .Should()
            .Be(
                new MusicDeviceDto("dev2", "Phone", "Smartphone", false, 0),
                "a null provider volume maps to the wire DTO's 0"
            );
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    public async Task An_unparseable_tenant_key_fails_closed_everywhere(string badKey)
    {
        (MusicService sut, RecordingHttpHandler handler) = Build(connectedService: "spotify");

        (await sut.PlayAsync(badKey)).ErrorCode.Should().Be("VALIDATION_FAILED");
        (await sut.SearchAsync(badKey, "query")).Should().BeEmpty();
        (await sut.GetNowPlayingAsync(badKey)).Should().BeNull();
        (await sut.SeekAsync(badKey, 1_000)).ErrorCode.Should().Be("VALIDATION_FAILED");

        handler.RequestUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_channel_with_no_connected_integration_resolves_no_provider()
    {
        // Spotify + YouTube are registered, but this channel connected neither → every call fails
        // closed with SERVICE_UNAVAILABLE (no active provider).
        (MusicService sut, RecordingHttpHandler handler) = Build(connectedService: null);

        (await sut.PlayAsync(ChannelId.ToString())).ErrorCode.Should().Be("SERVICE_UNAVAILABLE");
        (await sut.SearchAsync(ChannelId.ToString(), "query")).Should().BeEmpty();

        handler.RequestUrls.Should().BeEmpty();
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

    private static (MusicService Sut, RecordingHttpHandler Handler) Build(string? connectedService)
    {
        MusicTestDbContext db = new(
            new DbContextOptionsBuilder<MusicTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        if (connectedService is not null)
        {
            db.Services.Add(
                new Service
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = connectedService,
                    BroadcasterId = ChannelId,
                    Enabled = true,
                    AccessToken = "test-access-token",
                }
            );
            db.SaveChanges();
        }

        RecordingHttpHandler handler = new();
        SpotifyMusicProvider spotify = new(
            db,
            new PassthroughProtector(),
            new InMemoryIntegrationCapabilityStore(),
            new SingleHandlerClientFactory(handler),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance
        );
        YouTubeMusicProvider youtube = YouTubeProviderFactory.Create();

        MusicService sut = new(
            [spotify, youtube],
            db,
            new RecordingEventBus(),
            new BlockedTrackService(db),
            NullLogger<MusicService>.Instance
        );
        return (sut, handler);
    }
}
