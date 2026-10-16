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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves the public SR-page token service (music-sr.md §3.7): GetOrCreate mints once and is idempotent, Rotate
/// revokes by replacing the token, Resolve maps a live token to its channel context (name + enabled providers from
/// the music config) and fails NOT_FOUND for an unknown token — the JWT-less entry point for the /sr page.
/// </summary>
public sealed class SongRequestPageTokenServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f001");

    private static async Task<(
        SongRequestPageTokenService Sut,
        AuthDbContext Db,
        IMusicConfigService Config
    )> BuildAsync(
        bool seedChannel = true,
        bool spotifyConnected = false,
        string? youTubeApiKey = null
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        if (spotifyConnected)
            db.Services.Add(
                new()
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "spotify",
                    BroadcasterId = Channel,
                    Enabled = true,
                    AccessToken = "test-access-token",
                }
            );
        if (seedChannel)
        {
            db.Channels.Add(
                new()
                {
                    Id = Channel,
                    OwnerUserId = Guid.NewGuid(),
                    TwitchChannelId = "t1",
                    Name = "CoolStreamer",
                    NameNormalized = "coolstreamer",
                }
            );
            await db.SaveChangesAsync();
        }
        IMusicConfigService config = Substitute.For<IMusicConfigService>();
        IMusicProvider spotify = Substitute.For<IMusicProvider>();
        spotify.Provider.Returns("spotify");
        IMusicProvider youTube = YouTubeProviderFactory.Create(apiKey: youTubeApiKey, db: db);
        return (new(db, config, [spotify, youTube]), db, config);
    }

    [Fact]
    public async Task GetOrCreate_mints_once_and_is_idempotent()
    {
        (SongRequestPageTokenService sut, AuthDbContext db, _) = await BuildAsync();

        string first = (await sut.GetOrCreateAsync(Channel)).Value;
        string second = (await sut.GetOrCreateAsync(Channel)).Value;

        first.Should().NotBeNullOrWhiteSpace();
        second.Should().Be(first);
        db.Channels.Single().SongRequestPageToken.Should().Be(first);
    }

    [Fact]
    public async Task Rotate_replaces_the_token()
    {
        (SongRequestPageTokenService sut, _, _) = await BuildAsync();
        string original = (await sut.GetOrCreateAsync(Channel)).Value;

        string rotated = (await sut.RotateAsync(Channel)).Value;

        rotated.Should().NotBeNullOrWhiteSpace();
        rotated.Should().NotBe(original);
    }

    [Fact]
    public async Task Resolve_maps_the_channel_and_enabled_providers()
    {
        (SongRequestPageTokenService sut, _, IMusicConfigService config) = await BuildAsync(
            spotifyConnected: true,
            youTubeApiKey: "test-key"
        );
        config
            .GetConfigAsync(Channel.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(new MusicConfigDto(true, "spotify", 50, 5, false, true, "everyone"))
            );
        string token = (await sut.GetOrCreateAsync(Channel)).Value;

        SongRequestPageDto page = (await sut.ResolveAsync(token)).Value;

        page.BroadcasterId.Should().Be(Channel);
        page.ChannelName.Should().Be("CoolStreamer");
        page.IsAcceptingRequests.Should().BeTrue();
        page.EnabledProviders.Should().BeEquivalentTo("spotify"); // AllowSpotify=true, AllowYouTube=false
    }

    /// <summary>
    /// A YouTube-only channel: AllowSpotify is on by default, but Spotify was never connected, so a viewer who
    /// picked Spotify on the public page would only ever get a refusal. The page lists a provider only when the
    /// channel allows it AND the bot can reach it — YouTube on the API key, Spotify through its connection.
    /// </summary>
    [Fact]
    public async Task Resolve_offers_only_providers_the_channel_can_actually_use()
    {
        (SongRequestPageTokenService sut, _, IMusicConfigService config) = await BuildAsync(
            spotifyConnected: false,
            youTubeApiKey: "test-key"
        );
        config
            .GetConfigAsync(Channel.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(new MusicConfigDto(true, "auto", 50, 5, true, true, "everyone"))
            );
        string token = (await sut.GetOrCreateAsync(Channel)).Value;

        SongRequestPageDto page = (await sut.ResolveAsync(token)).Value;

        page.EnabledProviders.Should().Equal("youtube");
    }

    [Fact]
    public async Task Resolve_offers_no_YouTube_without_an_api_key_or_connection()
    {
        (SongRequestPageTokenService sut, _, IMusicConfigService config) = await BuildAsync(
            spotifyConnected: true,
            youTubeApiKey: null
        );
        config
            .GetConfigAsync(Channel.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(new MusicConfigDto(true, "auto", 50, 5, true, true, "everyone"))
            );
        string token = (await sut.GetOrCreateAsync(Channel)).Value;

        SongRequestPageDto page = (await sut.ResolveAsync(token)).Value;

        page.EnabledProviders.Should().Equal("spotify");
    }

    [Fact]
    public async Task Resolve_unknown_token_is_not_found()
    {
        (SongRequestPageTokenService sut, _, _) = await BuildAsync(seedChannel: false);

        Result<SongRequestPageDto> result = await sut.ResolveAsync("does-not-exist");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task ResolveByChannelName_finds_the_engaged_channel_case_insensitively_with_at_prefix()
    {
        (SongRequestPageTokenService sut, _, IMusicConfigService config) = await BuildAsync();
        config
            .GetConfigAsync(Channel.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(new MusicConfigDto(true, "spotify", 50, 5, false, true, "everyone"))
            );
        await sut.GetOrCreateAsync(Channel); // the operator engaged the SR page — a token exists

        SongRequestPageDto page = (await sut.ResolveByChannelNameAsync("@CoolStreamer")).Value;

        page.BroadcasterId.Should().Be(Channel);
        page.ChannelName.Should().Be("CoolStreamer");
        page.IsAcceptingRequests.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveByChannelName_never_exposes_a_channel_that_has_no_sr_page_token()
    {
        // The channel exists but never opened its SR page — the shareable name link must not make it
        // discoverable through a page it never set up.
        (SongRequestPageTokenService sut, _, _) = await BuildAsync();

        Result<SongRequestPageDto> result = await sut.ResolveByChannelNameAsync("coolstreamer");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }
}
