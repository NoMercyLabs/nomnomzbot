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

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Music;
using NSubstitute;
using ContractPlaylist = NomNomzBot.Application.Contracts.Music.MusicPlaylistDto;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// <c>!banger</c> adds the playing track to the channel's playlist: its own, or one made on first use. A
/// track that is already there is never added twice and gets the snarky reply.
/// </summary>
public sealed class BangerBuiltinTests : IDisposable
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-000000009903");
    private const string TrackUri = "spotify:track:rick1";
    private const string TrackName = "Never Gonna Give You Up";
    private const string TrackArtist = "Rick Astley";
    private const string PlaylistName = "Road Trip";

    private readonly MusicConfigDbFixture _config = new();
    private readonly IMusicService _music = Substitute.For<IMusicService>();
    private readonly IMusicProviderManageApi _playlists = Substitute.For<IMusicProviderManageApi>();

    public BangerBuiltinTests() =>
        _playlists
            .ListPlaylistsAsync(Broadcaster, "spotify", Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<ContractPlaylist>>([
                    new ContractPlaylist("pl1", PlaylistName, null, false, 12, null, "spotify"),
                ])
            );

    public void Dispose() => _config.Dispose();

    private BangerBuiltin Sut() =>
        new(
            _music,
            _playlists,
            _config.Service,
            TestBuiltinComposer.Create(),
            NullLogger<BangerBuiltin>.Instance
        );

    private static BuiltinCommandContext Context(string personality = PersonalityTone.Sassy) =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeringUserId = "viewer-1",
            TriggeringUserDisplayName = "Viewer1",
            RoleLevel = 0,
            Personality = personality,
        };

    private void Playing() =>
        _music
            .GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                new NowPlaying(
                    TrackName: TrackName,
                    Artist: TrackArtist,
                    Album: null,
                    ImageUrl: null,
                    DurationMs: 213_000,
                    ProgressMs: 1_000,
                    IsPlaying: true,
                    Volume: 50,
                    RequestedBy: null,
                    Provider: "spotify",
                    TrackUri: TrackUri
                )
            );

    private Task ChoosePlaylist() =>
        _config.Service.UpdateConfigAsync(
            Broadcaster.ToString(),
            new UpdateMusicConfigDto
            {
                BangerPlaylistId = "pl1",
                BangerPlaylistProvider = "spotify",
            }
        );

    private void TrackPresence(string playlistId, Result<bool> answer) =>
        _playlists
            .IsTrackInPlaylistAsync(
                Broadcaster,
                "spotify",
                playlistId,
                TrackUri,
                Arg.Any<CancellationToken>()
            )
            .Returns(answer);

    private void AddSucceeds(string playlistId) =>
        _playlists
            .AddPlaylistTracksAsync(
                Broadcaster,
                "spotify",
                playlistId,
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

    private Task AddReceived(int times, string playlistId) =>
        _playlists
            .Received(times)
            .AddPlaylistTracksAsync(
                Broadcaster,
                "spotify",
                playlistId,
                Arg.Is<IReadOnlyList<string>>(u => u.SequenceEqual(new[] { TrackUri })),
                Arg.Any<CancellationToken>()
            );

    private static async Task<List<string>> TextsAsync(
        string slot,
        string playlistName = PlaylistName
    )
    {
        Dictionary<string, string> variables = new()
        {
            ["user"] = "Viewer1",
            ["track.name"] = TrackName,
            ["track.artist"] = TrackArtist,
            ["playlist.name"] = playlistName,
        };
        TemplateResolver resolver = TestTemplateResolver.Create();
        List<string> texts = [];
        foreach (
            string template in ToneTemplateCatalog.Get(
                PersonalityTone.Sassy,
                BuiltinResponseSlots.Banger.Key,
                slot
            )
        )
            texts.Add(await resolver.ResolveAsync(template, variables, null));
        return texts;
    }

    [Fact]
    public async Task A_new_track_is_added_to_the_configured_playlist_once()
    {
        Playing();
        await ChoosePlaylist();
        TrackPresence("pl1", Result.Success(false));
        AddSucceeds("pl1");

        Result<string> reply = await Sut().ExecuteAsync(Context());

        await AddReceived(1, "pl1");
        (await TextsAsync(BuiltinResponseSlots.Banger.Added)).Should().Contain(reply.Value);
        reply.Value.Should().Contain(TrackName);
    }

    [Theory]
    [InlineData(PersonalityTone.Informative)]
    [InlineData(PersonalityTone.Friendly)]
    [InlineData(PersonalityTone.Sassy)]
    [InlineData(PersonalityTone.Hype)]
    [InlineData(PersonalityTone.Chill)]
    public async Task The_added_reply_names_the_viewer_the_track_its_artist_and_the_playlist(
        string personality
    )
    {
        Playing();
        await ChoosePlaylist();
        TrackPresence("pl1", Result.Success(false));
        AddSucceeds("pl1");

        Result<string> reply = await Sut().ExecuteAsync(Context(personality));

        reply.Value.Should().Contain("Viewer1");
        reply.Value.Should().Contain(TrackName);
        reply.Value.Should().Contain(TrackArtist);
        reply.Value.Should().Contain(PlaylistName);
    }

    [Theory]
    [InlineData(PersonalityTone.Informative)]
    [InlineData(PersonalityTone.Friendly)]
    [InlineData(PersonalityTone.Sassy)]
    [InlineData(PersonalityTone.Hype)]
    [InlineData(PersonalityTone.Chill)]
    public async Task The_already_there_reply_names_the_viewer_the_track_its_artist_and_the_playlist(
        string personality
    )
    {
        Playing();
        await ChoosePlaylist();
        TrackPresence("pl1", Result.Success(true));

        Result<string> reply = await Sut().ExecuteAsync(Context(personality));

        reply.Value.Should().Contain("Viewer1");
        reply.Value.Should().Contain(TrackName);
        reply.Value.Should().Contain(TrackArtist);
        reply.Value.Should().Contain(PlaylistName);
    }

    [Fact]
    public async Task A_track_already_in_the_playlist_is_not_added_and_gets_a_snarky_reply_with_the_name()
    {
        Playing();
        await ChoosePlaylist();
        TrackPresence("pl1", Result.Success(true));

        Result<string> reply = await Sut().ExecuteAsync(Context());

        await AddReceived(0, "pl1");
        (await TextsAsync(BuiltinResponseSlots.Banger.AlreadyThere))
            .Should()
            .HaveCount(10)
            .And.Contain(reply.Value);
        reply.Value.Should().Contain("@Viewer1");
    }

    [Fact]
    public async Task With_no_playlist_and_auto_create_on_one_is_made_saved_in_the_config_and_filled()
    {
        Playing();
        await _config.Service.UpdateConfigAsync(
            Broadcaster.ToString(),
            new UpdateMusicConfigDto { BangerAutoCreate = true }
        );
        _playlists
            .CreatePlaylistAsync(
                Broadcaster,
                "spotify",
                Arg.Any<CreateMusicPlaylistDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new ContractPlaylist("new-pl", "Bangers", null, false, 0, null, "spotify")
                )
            );
        _playlists
            .ListPlaylistsAsync(Broadcaster, "spotify", Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<ContractPlaylist>>([
                    new ContractPlaylist("pl1", PlaylistName, null, false, 12, null, "spotify"),
                    new ContractPlaylist("new-pl", "Bangers", null, false, 0, null, "spotify"),
                ])
            );
        TrackPresence("new-pl", Result.Success(false));
        AddSucceeds("new-pl");

        Result<string> reply = await Sut().ExecuteAsync(Context());

        await _playlists
            .Received(1)
            .CreatePlaylistAsync(
                Broadcaster,
                "spotify",
                Arg.Any<CreateMusicPlaylistDto>(),
                Arg.Any<CancellationToken>()
            );
        MusicConfigDto saved = (await _config.Service.GetConfigAsync(Broadcaster.ToString())).Value;
        saved.BangerPlaylistId.Should().Be("new-pl");
        saved.BangerPlaylistProvider.Should().Be("spotify");
        await AddReceived(1, "new-pl");
        reply.Value.Should().Contain("Bangers");
        (await TextsAsync(BuiltinResponseSlots.Banger.Added, "Bangers"))
            .Should()
            .Contain(reply.Value);
    }

    [Fact]
    public async Task With_no_playlist_and_auto_create_off_the_provider_is_never_called_and_the_reply_asks_for_a_playlist()
    {
        Playing();

        Result<string> reply = await Sut().ExecuteAsync(Context());

        _playlists.ReceivedCalls().Should().BeEmpty();
        (await TextsAsync(BuiltinResponseSlots.Banger.NoPlaylist)).Should().Contain(reply.Value);
    }

    [Fact]
    public async Task With_nothing_playing_the_provider_is_never_called_and_the_reply_says_so()
    {
        _music
            .GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns((NowPlaying?)null);

        Result<string> reply = await Sut().ExecuteAsync(Context());

        _playlists.ReceivedCalls().Should().BeEmpty();
        (await TextsAsync(BuiltinResponseSlots.Banger.Nothing)).Should().Contain(reply.Value);
    }

    [Fact]
    public async Task A_provider_failure_gets_the_failed_reply_and_no_add()
    {
        Playing();
        await ChoosePlaylist();
        TrackPresence("pl1", Result.Failure<bool>("no scope", "MISSING_SCOPE"));

        Result<string> reply = await Sut().ExecuteAsync(Context());

        await AddReceived(0, "pl1");
        (await TextsAsync(BuiltinResponseSlots.Banger.Failed)).Should().Contain(reply.Value);
    }
}
