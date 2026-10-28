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
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Notifications;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

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
        // YouTube-only channel: pause, resume, skip and volume ride the overlay player (YouTubeTransportControlTests);
        // every other transport member gates off (§3.5) with CAPABILITY_UNSUPPORTED — no exception.
        (MusicService sut, RecordingHttpHandler handler) = Build(connectedService: "youtube");
        string channel = ChannelId.ToString();

        (await sut.PreviousAsync(channel)).ErrorCode.Should().Be("CAPABILITY_UNSUPPORTED");
        (await sut.SetShuffleAsync(channel, true)).ErrorCode.Should().Be("CAPABILITY_UNSUPPORTED");
        (await sut.SetRepeatAsync(channel, "track"))
            .ErrorCode.Should()
            .Be("CAPABILITY_UNSUPPORTED");
        (await sut.TransferPlaybackAsync(channel, "device-1"))
            .ErrorCode.Should()
            .Be("CAPABILITY_UNSUPPORTED");
        (await sut.GetDevicesAsync(channel)).Should().BeEmpty();
        (await sut.GetPlaylistsAsync(channel)).Should().BeEmpty();
        (await sut.PlayContextAsync(channel, "spotify:playlist:xyz")).Should().BeFalse();

        handler.RequestUrls.Should().BeEmpty("a gated member must never reach a provider API");
    }

    [Fact]
    public async Task Seek_reaches_the_playing_engine_in_milliseconds_not_the_provider()
    {
        (MusicService sut, RecordingHttpHandler handler, DirectorRig rig) = BuildWithRig();
        FakeEngine engine = await StartOwnerSpotifyAsync(rig);

        Result ok = await sut.SeekAsync(ChannelId.ToString(), 93_500);

        ok.IsSuccess.Should().BeTrue();
        engine.Calls.Should().EndWith(new EngineCall("Seek", "93500"));
        handler.RequestUrls.Should().BeEmpty("the engine, not the provider, carries the control");
    }

    [Fact]
    public async Task Volume_reaches_the_playing_engine_with_the_exact_level()
    {
        (MusicService sut, RecordingHttpHandler handler, DirectorRig rig) = BuildWithRig();
        FakeEngine engine = await StartOwnerSpotifyAsync(rig);

        Result ok = await sut.SetVolumeAsync(ChannelId.ToString(), 63);

        ok.IsSuccess.Should().BeTrue();
        engine.Calls.Should().EndWith(new EngineCall("SetVolume", "63"));
        handler.RequestUrls.Should().BeEmpty("the engine, not the provider, carries the control");
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
    public async Task Previous_early_in_the_song_restarts_it_on_the_playing_engine()
    {
        (MusicService sut, RecordingHttpHandler handler, DirectorRig rig) = BuildWithRig();
        FakeEngine engine = await StartOwnerSpotifyAsync(rig);

        Result ok = await sut.PreviousAsync(ChannelId.ToString());

        ok.IsSuccess.Should().BeTrue();
        engine.Calls.Should().EndWith(new EngineCall("Seek", "0"));
        handler
            .RequestUrls.Should()
            .NotContain(
                url => url.Contains("/me/player/previous") || url.Contains("/me/player/seek"),
                "the engine, not the provider, carries the control (the GET is the state re-read)"
            );
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

    [Fact]
    public async Task Playlists_carry_the_provider_key_of_the_active_provider()
    {
        (MusicService sut, RecordingHttpHandler handler) = Build(connectedService: "spotify");
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/playlists", StringComparison.Ordinal),
            HttpStatusCode.OK,
            """
            {"items":[
              {"id":"pl1","name":"Bangers","uri":"spotify:playlist:pl1","tracks":{"total":12}}
            ]}
            """
        );

        IReadOnlyList<MusicPlaylistDto> playlists = await sut.GetPlaylistsAsync(
            ChannelId.ToString()
        );

        playlists.Should().ContainSingle();
        playlists[0]
            .Should()
            .Be(
                new MusicPlaylistDto("pl1", "Bangers", "spotify:playlist:pl1", 12, null, "spotify")
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
    public async Task Embedded_playback_token_succeeds_for_a_connected_spotify_channel_with_the_streaming_scope()
    {
        // SpotifyMusicProvider.Capabilities must declare EmbeddedPlayback, or MusicService's own
        // HasCapability gate rejects every request with CAPABILITY_UNSUPPORTED before the provider's
        // streaming-scope check ever runs — regressed once already (the flag was missing entirely
        // while GetEmbeddedPlaybackTokenAsync was fully implemented underneath it).
        (MusicService sut, RecordingHttpHandler handler) = Build(
            connectedService: "spotify",
            scopes: ["streaming"]
        );
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/api/token", StringComparison.Ordinal),
            HttpStatusCode.OK,
            """{"access_token":"refreshed-token","token_type":"Bearer","expires_in":3600}"""
        );

        Result<string> result = await sut.GetEmbeddedPlaybackTokenAsync(ChannelId.ToString());

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public async Task A_channel_with_no_connected_integration_falls_to_keyless_youtube_and_never_reaches_spotify()
    {
        // Spotify + YouTube are registered and this channel connected neither. YouTube works with no
        // connection, so it is the active provider: Play reaches YouTube's player (no overlay open, so
        // no device) instead of failing as "no provider", and Spotify is never called.
        (MusicService sut, RecordingHttpHandler handler) = Build(connectedService: null);

        (await sut.PlayAsync(ChannelId.ToString())).ErrorCode.Should().Be("NO_ACTIVE_DEVICE");
        (await sut.SearchAsync(ChannelId.ToString(), "query")).Should().BeEmpty();

        handler
            .RequestUrls.Should()
            .NotContain(url => url.Contains("spotify.com", StringComparison.Ordinal))
            .And.NotContain(url => url.Contains("googleapis.com", StringComparison.Ordinal));
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

    private static (MusicService Sut, RecordingHttpHandler Handler) Build(
        string? connectedService,
        string[]? scopes = null
    )
    {
        (MusicService sut, RecordingHttpHandler handler, _) = BuildWithRig(
            connectedService,
            scopes
        );
        return (sut, handler);
    }

    /// <summary>The owner plays Spotify music: the director observes it and goes Playing on the Spotify engine.</summary>
    private static async Task<FakeEngine> StartOwnerSpotifyAsync(DirectorRig rig)
    {
        FakeEngine engine = rig.Engines.EngineOf(ChannelId, EngineKind.Spotify);
        await rig.Registry.SendAsync(ChannelId, new OutputAttached());
        await engine.EmitAsync(
            new EngineReport(EngineKind.Spotify, "spotify:track:x", true, 1_000, 200_000)
        );

        return engine;
    }

    private static (MusicService Sut, RecordingHttpHandler Handler, DirectorRig Rig) BuildWithRig(
        string? connectedService = "spotify",
        string[]? scopes = null
    )
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        FakeIntegrationTokenVault vault = new(db);
        if (connectedService is not null)
        {
            // Routing seed: MusicService.GetActiveProviderAsync selects the active provider by which
            // Service names are connected — a separate concern from SpotifyMusicProvider's OWN token
            // resolution (which reads the vault, S003).
            db.Services.Add(
                new()
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = connectedService,
                    BroadcasterId = ChannelId,
                    Enabled = true,
                    AccessToken = "test-access-token",
                    Scopes = scopes ?? [],
                }
            );
            db.SaveChanges();

            if (connectedService == "spotify")
                vault.SeedConnectedSpotify(ChannelId, scopes: scopes);
        }

        RecordingHttpHandler handler = new();
        SpotifyMusicProvider spotify = new(
            db,
            vault,
            new InMemoryIntegrationCapabilityStore(),
            new LastActiveSpotifyDeviceTracker(),
            new SpotifyRateLimitCooldowns(new NullActionRequiredChangeNotifier()),
            new SingleHandlerClientFactory(handler),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance,
            NullSystemCredentialsProvider.Instance,
            new ConnectionRefreshGate(),
            new NullChannelCredentialsResolver(NullSystemCredentialsProvider.Instance),
            new OutboundSanctionAccessor()
        );
        YouTubeMusicProvider youtube = YouTubeProviderFactory.Create();

        MusicService sut = new(
            [spotify, youtube],
            db,
            new RecordingEventBus(),
            new BlockedTrackService(db),
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
        return (sut, handler, DirectorRig.Of(directorStore));
    }
}
