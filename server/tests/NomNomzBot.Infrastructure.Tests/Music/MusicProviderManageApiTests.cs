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
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves the §3.10 manage surface's capability gating (music-sr.md): an unsupported member fails
/// with <c>CAPABILITY_UNSUPPORTED</c> — never a throw — an unregistered provider key fails
/// <c>NOT_FOUND</c>, an unconnected provider fails <c>MISSING_SCOPE</c>, and Spotify playlist
/// listing maps the provider payload field-by-field. Uses the REAL providers (Spotify over a
/// stubbed HTTP transport, the real unconfigured YouTube provider) through the real gating front, so the tests
/// prove production wiring, not substitutes. The wired Spotify manage WRITES are covered in
/// <see cref="SpotifyMusicProviderManageWriteTests"/>.
/// </summary>
public sealed class MusicProviderManageApiTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f2001");

    [Fact]
    public async Task Unsupported_member_fails_with_CAPABILITY_UNSUPPORTED_and_never_throws()
    {
        // YouTube has no artist-follow analogue (channels only). A Channel-target follow gates on
        // Subscriptions and an artist-target follow gates on Library (both declared) so the FRONT
        // passes — the provider itself then fails closed with CAPABILITY_UNSUPPORTED, never a throw.
        (MusicProviderManageApi api, _, RecordingHttpHandler handler) = Build(connectSpotify: true);

        Func<Task<Result>> act = () =>
            api.FollowAsync(ChannelId, "youtube", MusicFollowTarget.Artist, "some-artist");

        Result result = (await act.Should().NotThrowAsync()).Subject;
        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("CAPABILITY_UNSUPPORTED");
        handler
            .RequestUrls.Should()
            .BeEmpty("an unsupported target must never reach a provider API");
    }

    [Fact]
    public async Task YouTube_playlist_listing_now_flips_past_the_front_and_needs_a_connection()
    {
        // The YouTube manage slice landed: Playlists is now declared, so the front no longer returns
        // CAPABILITY_UNSUPPORTED — the call reaches the provider, which (with no youtube connection)
        // fails MISSING_SCOPE. This proves the capability flip THROUGH the real gating front.
        (MusicProviderManageApi api, _, _) = Build(connectSpotify: true);

        Result<IReadOnlyList<MusicPlaylistDto>> result = await api.ListPlaylistsAsync(
            ChannelId,
            "youtube"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("MISSING_SCOPE");
    }

    [Fact]
    public async Task Unregistered_provider_key_fails_closed_with_NOT_FOUND()
    {
        (MusicProviderManageApi api, _, _) = Build(connectSpotify: true);

        Result<IReadOnlyList<MusicPlaylistDto>> result = await api.ListPlaylistsAsync(
            ChannelId,
            "pretzel"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Channel_follow_resolves_to_the_Subscriptions_capability_and_gates_on_it()
    {
        // Spotify has no channel-subscription analogue (§3.5) — a Channel-target follow must gate on
        // Subscriptions (absent) even though Spotify declares Library and Playlists.
        (MusicProviderManageApi api, _, RecordingHttpHandler handler) = Build(connectSpotify: true);

        Result result = await api.FollowAsync(
            ChannelId,
            "spotify",
            MusicFollowTarget.Channel,
            "some-channel"
        );

        result.ErrorCode.Should().Be("CAPABILITY_UNSUPPORTED");
        handler.RequestUrls.Should().BeEmpty("a gated member must never reach the provider's API");
    }

    [Fact]
    public async Task Spotify_playlist_listing_maps_the_provider_payload_field_by_field()
    {
        (MusicProviderManageApi api, _, RecordingHttpHandler handler) = Build(connectSpotify: true);
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/playlists", StringComparison.Ordinal),
            HttpStatusCode.OK,
            """
            {"items":[
              {"id":"pl1","name":"Stream Bangers","uri":"spotify:playlist:pl1","description":"Hype tracks",
               "public":true,"images":[{"url":"https://i.scdn.co/image/pl1.jpg"}],"tracks":{"total":42}},
              {"id":"pl2","name":"Chill","uri":"spotify:playlist:pl2","description":"",
               "public":false,"images":[],"tracks":{"total":7}}
            ]}
            """
        );

        Result<IReadOnlyList<MusicPlaylistDto>> result = await api.ListPlaylistsAsync(
            ChannelId,
            "spotify"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);

        MusicPlaylistDto first = result.Value[0];
        first.Id.Should().Be("pl1");
        first.Name.Should().Be("Stream Bangers");
        first.Description.Should().Be("Hype tracks");
        first.IsPublic.Should().BeTrue();
        first.TrackCount.Should().Be(42);
        first.ImageUrl.Should().Be("https://i.scdn.co/image/pl1.jpg");
        first.Provider.Should().Be("spotify");

        MusicPlaylistDto second = result.Value[1];
        second.Description.Should().BeNull("Spotify's empty description means 'none'");
        second.IsPublic.Should().BeFalse();
        second.TrackCount.Should().Be(7);
        second.ImageUrl.Should().BeNull();
    }

    [Fact]
    public async Task Spotify_playlist_listing_without_a_connection_fails_with_MISSING_SCOPE()
    {
        (MusicProviderManageApi api, _, _) = Build(connectSpotify: false);

        Result<IReadOnlyList<MusicPlaylistDto>> result = await api.ListPlaylistsAsync(
            ChannelId,
            "spotify"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("MISSING_SCOPE");
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

    private static (
        MusicProviderManageApi Api,
        MusicTestDbContext Db,
        RecordingHttpHandler Handler
    ) Build(bool connectSpotify)
    {
        MusicTestDbContext db = new(
            new DbContextOptionsBuilder<MusicTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        if (connectSpotify)
        {
            db.Services.Add(
                new Service
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "spotify",
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

        MusicProviderManageApi api = new([spotify, youtube]);
        return (api, db, handler);
    }
}
