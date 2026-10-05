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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NomNomzBot.Infrastructure.Tests.Music;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// <c>!playlist</c> (legacy parity) answers with the link to the channel's bangers playlist, built from the
/// configured playlist id and provider, and says so plainly when none is configured or the provider has no
/// public link form. Replies use the old bot's wording in the informative tone.
/// </summary>
public sealed class PlaylistBuiltinTests : IDisposable
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-00000000a601");

    private readonly MusicConfigDbFixture _config = new();
    private readonly IMusicService _music = Substitute.For<IMusicService>();

    public void Dispose() => _config.Dispose();

    private PlaylistBuiltin Sut() => new(_music, _config.Service, FakeComposer());

    private static BuiltinCommandContext Context() =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeringUserId = "viewer-1",
            TriggeringUserDisplayName = "Viewer",
            Personality = PersonalityTone.Informative,
        };

    private static IBuiltinResponseComposer FakeComposer()
    {
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                string template = call.ArgAt<string>(0);
                foreach (
                    KeyValuePair<string, string> kvp in call.ArgAt<IDictionary<string, string>>(1)
                )
                    template = template.Replace($"{{{kvp.Key}}}", kvp.Value);
                return Task.FromResult(template);
            });
        return new BuiltinResponseComposer(
            resolver,
            NoPlatformBuiltinReplies.Instance,
            FakeChannelBuiltinReplies.None
        );
    }

    private Task Choose(string? id, string? provider) =>
        _config.Service.UpdateConfigAsync(
            Broadcaster.ToString(),
            new UpdateMusicConfigDto { BangerPlaylistId = id, BangerPlaylistProvider = provider }
        );

    [Fact]
    public async Task Spotify_playlist_gives_the_open_spotify_link()
    {
        await Choose("37i9dQZF1DXcBWIGoYBM5M", "spotify");

        Result<string> result = await Sut().ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result
            .Value.Should()
            .Be(
                "The bangers playlist is: https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M"
            );
    }

    [Fact]
    public async Task Youtube_playlist_gives_the_youtube_playlist_link()
    {
        await Choose("PLabc123", "youtube");

        Result<string> result = await Sut().ExecuteAsync(Context());

        result
            .Value.Should()
            .Be("The bangers playlist is: https://www.youtube.com/playlist?list=PLabc123");
    }

    [Fact]
    public async Task Without_a_stored_provider_the_active_provider_decides_the_link()
    {
        await Choose("37i9dQZF1DXcBWIGoYBM5M", null);
        _music
            .GetActiveProviderKeyAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns("spotify");

        Result<string> result = await Sut().ExecuteAsync(Context());

        result
            .Value.Should()
            .Be(
                "The bangers playlist is: https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M"
            );
    }

    [Fact]
    public async Task No_playlist_configured_says_so()
    {
        Result<string> result = await Sut().ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("No playlist ID configured.");
    }

    [Fact]
    public async Task A_provider_without_a_public_link_form_says_playlist_not_found()
    {
        await Choose("some-id", "unknownprovider");

        Result<string> result = await Sut().ExecuteAsync(Context());

        result.Value.Should().Be("Playlist not found.");
    }

    [Fact]
    public async Task No_provider_at_all_says_playlist_not_found()
    {
        await Choose("some-id", null);
        _music
            .GetActiveProviderKeyAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        Result<string> result = await Sut().ExecuteAsync(Context());

        result.Value.Should().Be("Playlist not found.");
    }
}
