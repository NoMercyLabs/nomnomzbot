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
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves <see cref="MusicService"/>'s §3.5 capability gating over the widened provider seam: a
/// transport call on a provider lacking the capability fails closed (false/empty, never a throw,
/// never a provider API call), while Spotify transport rides the widened Guid-keyed members —
/// including the seam's whole-seconds seek contract observed on the actual wire. Uses the real
/// providers (YouTube stub, Spotify over stubbed HTTP), not substitutes.
/// </summary>
public sealed class MusicServiceCapabilityGatingTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f4001");

    [Fact]
    public async Task Transport_members_fail_closed_on_a_provider_without_the_capability()
    {
        // YouTube-only channel: the YouTube Data API has no playback transport, so every
        // transport member gates off (§3.5) — false/empty, no exception.
        (MusicService sut, RecordingSpotifyHandler handler) = Build(connectedService: "youtube");
        string channel = ChannelId.ToString();

        (await sut.PlayAsync(channel)).Should().BeFalse();
        (await sut.PauseAsync(channel)).Should().BeFalse();
        (await sut.SkipAsync(channel)).Should().BeFalse();
        (await sut.SeekAsync(channel, 5_000)).Should().BeFalse();
        (await sut.SetShuffleAsync(channel, true)).Should().BeFalse();
        (await sut.SetRepeatAsync(channel, "track")).Should().BeFalse();
        (await sut.SetVolumeAsync(channel, 50)).Should().BeFalse();
        (await sut.TransferPlaybackAsync(channel, "device-1")).Should().BeFalse();
        (await sut.GetDevicesAsync(channel)).Should().BeEmpty();
        (await sut.GetPlaylistsAsync(channel)).Should().BeEmpty();
        (await sut.PlayContextAsync(channel, "spotify:playlist:xyz")).Should().BeFalse();

        handler.RequestUrls.Should().BeEmpty("a gated member must never reach a provider API");
    }

    [Fact]
    public async Task Seek_crosses_the_seam_in_whole_seconds_and_reaches_Spotify_in_milliseconds()
    {
        (MusicService sut, RecordingSpotifyHandler handler) = Build(connectedService: "spotify");

        bool ok = await sut.SeekAsync(ChannelId.ToString(), 93_500);

        ok.Should().BeTrue();
        handler
            .RequestUrls.Should()
            .ContainSingle(url => url.Contains("/me/player/seek?position_ms=93000"));
    }

    [Fact]
    public async Task Negative_seek_fails_closed_without_an_API_call()
    {
        (MusicService sut, RecordingSpotifyHandler handler) = Build(connectedService: "spotify");

        bool ok = await sut.SeekAsync(ChannelId.ToString(), -1);

        ok.Should().BeFalse();
        handler.RequestUrls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("off")]
    [InlineData("track")]
    [InlineData("Context")]
    public async Task Repeat_mode_strings_map_onto_the_seam_enum_and_reach_Spotify(string mode)
    {
        (MusicService sut, RecordingSpotifyHandler handler) = Build(connectedService: "spotify");

        bool ok = await sut.SetRepeatAsync(ChannelId.ToString(), mode);

        ok.Should().BeTrue();
        handler
            .RequestUrls.Should()
            .ContainSingle(url =>
                url.Contains($"/me/player/repeat?state={mode.ToLowerInvariant()}")
            );
    }

    [Fact]
    public async Task Invalid_repeat_mode_fails_closed_without_an_API_call()
    {
        (MusicService sut, RecordingSpotifyHandler handler) = Build(connectedService: "spotify");

        bool ok = await sut.SetRepeatAsync(ChannelId.ToString(), "banana");

        ok.Should().BeFalse();
        handler.RequestUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task Devices_map_field_by_field_from_the_provider_payload()
    {
        (MusicService sut, RecordingSpotifyHandler handler) = Build(connectedService: "spotify");
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
        (MusicService sut, RecordingSpotifyHandler handler) = Build(connectedService: "spotify");

        (await sut.PlayAsync(badKey)).Should().BeFalse();
        (await sut.SearchAsync(badKey, "query")).Should().BeEmpty();
        (await sut.GetNowPlayingAsync(badKey)).Should().BeNull();
        (await sut.SeekAsync(badKey, 1_000)).Should().BeFalse();

        handler.RequestUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_channel_with_no_connected_integration_resolves_no_provider()
    {
        // Spotify + YouTube are registered, but this channel connected neither → every call fails
        // closed (unknown-tenant-key resolution finds nothing).
        (MusicService sut, RecordingSpotifyHandler handler) = Build(connectedService: null);

        (await sut.PlayAsync(ChannelId.ToString())).Should().BeFalse();
        (await sut.SearchAsync(ChannelId.ToString(), "query")).Should().BeEmpty();

        handler.RequestUrls.Should().BeEmpty();
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

    private static (MusicService Sut, RecordingSpotifyHandler Handler) Build(
        string? connectedService
    )
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

        RecordingSpotifyHandler handler = new();
        SpotifyMusicProvider spotify = new(
            db,
            new PassthroughProtector(),
            new SingleHandlerClientFactory(handler),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance
        );
        YouTubeMusicProvider youtube = new(NullLogger<YouTubeMusicProvider>.Instance);

        MusicService sut = new(
            [spotify, youtube],
            db,
            new RecordingEventBus(),
            NullLogger<MusicService>.Instance
        );
        return (sut, handler);
    }
}
