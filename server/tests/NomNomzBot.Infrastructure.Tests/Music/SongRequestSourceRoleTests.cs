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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Song requests have one request role per music service: <c>music:request:spotify</c> is open to everyone by
/// default, <c>music:request:youtube</c> is broadcaster-only until the channel raises its reach. The YouTube key
/// is checked on top of the channel's AllowYouTube switch, so both must allow.
/// </summary>
public sealed class SongRequestSourceRoleTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-00000000ab78");
    private const string SpotifyUri = "spotify:track:abc";
    private const string YouTubeUri = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";
    private const int Viewer = 0;
    private const int Moderator = 10;
    private const MusicProviderCapabilities Transport =
        MusicProviderCapabilities.PlaybackControl
        | MusicProviderCapabilities.Skip
        | MusicProviderCapabilities.AcceptsSongRequests
        | MusicProviderCapabilities.Search
        | MusicProviderCapabilities.Queue;

    private readonly SongRequestQueueStore _store = new();
    private MusicTestDbContext? _db;

    private sealed class FixedConfigService(MusicConfigDto config) : IMusicConfigService
    {
        public Task<Result<MusicConfigDto>> GetConfigAsync(
            string broadcasterId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Success(config));

        public Task<Result<MusicConfigDto>> UpdateConfigAsync(
            string broadcasterId,
            UpdateMusicConfigDto request,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public MusicConfigDto GetDefaults() => config;
    }

    private static IMusicProvider Provider(string key, string ownedUri)
    {
        IMusicProvider provider = Substitute.For<IMusicProvider, IMusicProviderPlayerGate>();
        provider.Provider.Returns(key);
        provider.Capabilities.Returns(Transport);
        provider.OwnsTrackUri(Arg.Any<string>()).Returns(call => (string)call[0] == ownedUri);
        provider
            .WorksWithoutConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(key == "youtube");
        provider
            .AddToQueueAsync(ChannelId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        provider
            .ResolveTrackAsync(ChannelId, ownedUri, Arg.Any<CancellationToken>())
            .Returns(
                (
                    new TrackInfo
                    {
                        TrackName = "Song",
                        Artist = "Artist",
                        Album = "",
                        TrackUri = ownedUri,
                        Provider = key,
                        DurationMs = 200_000,
                    },
                    MusicProviderFailureReason.None
                )
            );
        ((IMusicProviderPlayerGate)provider)
            .HasPlayerAsync(ChannelId, Arg.Any<CancellationToken>())
            .Returns(true);
        return provider;
    }

    private MusicService NewService(
        int? youTubeOverride = null,
        bool allowYouTube = true,
        string oldFloor = "everyone"
    )
    {
        _db = MusicTestDbContext.New();
        _db.Services.Add(
            new()
            {
                Id = Guid.NewGuid().ToString(),
                Name = "spotify",
                BroadcasterId = ChannelId,
                Enabled = true,
                AccessToken = "test-access-token",
            }
        );
        if (youTubeOverride is { } level)
            _db.ChannelActionOverrides.Add(
                new()
                {
                    BroadcasterId = ChannelId,
                    ActionDefinitionId = _db
                        .ActionDefinitions.Single(a => a.ActionKey == "music:request:youtube")
                        .Id,
                    OverrideLevel = level,
                }
            );
        _db.SaveChanges();

        MusicConfigDto config = new(true, "auto", 50, 5, allowYouTube, true, oldFloor);
        return new(
            [Provider("spotify", SpotifyUri), Provider("youtube", YouTubeUri)],
            _db,
            new RecordingEventBus(),
            new BlockedTrackService(_db),
            _store,
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            new FixedConfigService(config),
            Substitute.For<ICurrencyAccountService>(),
            new NowPlayingCache(),
            new OutboundSanctionAccessor(),
            Substitute.For<IUserIdentityService>(),
            Substitute.For<IForeignLinkTitleLookup>(),
            DirectorRig.Of(_store).Registry,
            DirectorRig.Of(_store).Registry
        );
    }

    private IReadOnlyList<string> Queued() =>
        _store
            .GetOrCreate(ChannelId.ToString())
            .GetSnapshot()
            .Select(e => e.Item.TrackUri)
            .ToList();

    [Fact]
    public async Task A_viewer_can_request_a_spotify_link_by_default()
    {
        MusicService sut = NewService();

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            SpotifyUri,
            "viewer",
            Viewer
        );

        result.IsSuccess.Should().BeTrue();
        Queued().Should().Equal(SpotifyUri);
    }

    [Fact]
    public async Task The_old_global_floor_no_longer_gates_a_request_the_source_rule_allows()
    {
        MusicService sut = NewService(oldFloor: "moderators");

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            SpotifyUri,
            "viewer",
            Viewer
        );

        result.IsSuccess.Should().BeTrue();
        Queued().Should().Equal(SpotifyUri);
    }

    [Fact]
    public async Task A_viewer_is_refused_a_youtube_link_by_default_even_when_the_channel_allows_youtube()
    {
        MusicService sut = NewService(allowYouTube: true);

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            YouTubeUri,
            "viewer",
            Viewer
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("SOURCE_ROLE_REQUIRED");
        Queued().Should().BeEmpty();
    }

    [Fact]
    public async Task Raising_the_youtube_key_to_everyone_lets_the_viewer_request_it()
    {
        MusicService sut = NewService(youTubeOverride: Viewer);

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            YouTubeUri,
            "viewer",
            Viewer
        );

        result.IsSuccess.Should().BeTrue();
        Queued().Should().Equal(YouTubeUri);
    }

    [Fact]
    public async Task A_youtube_key_open_to_everyone_still_obeys_the_channels_allow_switch()
    {
        MusicService sut = NewService(youTubeOverride: Viewer, allowYouTube: false);

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            YouTubeUri,
            "viewer",
            Viewer
        );

        result.ErrorCode.Should().Be("PROVIDER_NOT_ALLOWED");
        Queued().Should().BeEmpty();
    }

    [Fact]
    public async Task A_moderator_level_youtube_key_admits_a_moderator_and_refuses_a_viewer()
    {
        MusicService sut = NewService(youTubeOverride: Moderator);

        Result<MusicTrack> viewer = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            YouTubeUri,
            "viewer",
            Viewer
        );
        Result<MusicTrack> moderator = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            YouTubeUri,
            "mod",
            Moderator
        );

        viewer.ErrorCode.Should().Be("SOURCE_ROLE_REQUIRED");
        moderator.IsSuccess.Should().BeTrue();
        Queued().Should().Equal(YouTubeUri);
    }

    [Fact]
    public void The_seeded_defaults_are_everyone_for_spotify_and_broadcaster_for_youtube()
    {
        NewService();

        ActionDefinition spotify = _db!.ActionDefinitions.Single(a =>
            a.ActionKey == "music:request:spotify"
        );
        ActionDefinition youTube = _db.ActionDefinitions.Single(a =>
            a.ActionKey == "music:request:youtube"
        );

        (spotify.DefaultLevel, spotify.FloorLevel).Should().Be((0, 0));
        (youTube.DefaultLevel, youTube.FloorLevel).Should().Be((40, 0));
    }
}
