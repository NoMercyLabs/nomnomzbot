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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NomNomzBot.Infrastructure.Tests.Music;
using NSubstitute;
using ContractPlaylist = NomNomzBot.Application.Contracts.Music.MusicPlaylistDto;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// The banger target setting: the default saves to the provider's likes, and the opt-in modes keep one
/// playlist per requester on the playing song's service, made on first use and found by name after.
/// </summary>
public sealed class BangerTargetTests : IDisposable
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-000000009911");
    private const string User = "Zorblax99";
    private const string TrackName = "Test Anthem";

    private readonly MusicConfigDbFixture _config = new();
    private readonly IMusicService _music = Substitute.For<IMusicService>();
    private readonly IMusicProviderManageApi _manage = Substitute.For<IMusicProviderManageApi>();
    private readonly FakeTimeProvider _clock = new(new(2026, 11, 17, 20, 0, 0, TimeSpan.Zero));

    public void Dispose() => _config.Dispose();

    private BangerBuiltin Sut() =>
        new(
            _music,
            _manage,
            _config.Service,
            TestBuiltinComposer.Create(),
            NullLogger<BangerBuiltin>.Instance,
            _clock
        );

    private static BuiltinCommandContext Context() =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeringUserId = "viewer-zorblax",
            TriggeringUserDisplayName = User,
            RoleLevel = 0,
            Personality = PersonalityTone.Sassy,
        };

    private void Playing(string provider, string uri)
    {
        _music
            .GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                new NowPlaying(
                    TrackName: TrackName,
                    Artist: "Test Artist",
                    Album: null,
                    ImageUrl: null,
                    DurationMs: 200_000,
                    ProgressMs: 1_000,
                    IsPlaying: true,
                    Volume: 50,
                    RequestedBy: null,
                    Provider: provider,
                    TrackUri: uri
                )
            );
    }

    private Task Target(string target) =>
        _config.Service.UpdateConfigAsync(
            Broadcaster.ToString(),
            new UpdateMusicConfigDto { BangerTarget = target }
        );

    private void Existing(string provider, params ContractPlaylist[] lists) =>
        _manage
            .ListPlaylistsAsync(Broadcaster, provider, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<ContractPlaylist>>(lists));

    private void CreateReturns(string provider, string id, string name) =>
        _manage
            .CreatePlaylistAsync(
                Broadcaster,
                provider,
                Arg.Any<CreateMusicPlaylistDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(new ContractPlaylist(id, name, null, false, 0, null, provider))
            );

    private void PlaylistAcceptsTrack(string provider, string id)
    {
        _manage
            .IsTrackInPlaylistAsync(
                Broadcaster,
                provider,
                id,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(false));
        _manage
            .AddPlaylistTracksAsync(
                Broadcaster,
                provider,
                id,
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
    }

    private static ContractPlaylist Playlist(string provider, string id, string name) =>
        new(id, name, null, false, 3, null, provider);

    [Fact]
    public async Task Per_year_on_spotify_makes_a_named_playlist_for_the_requester_and_adds_the_song()
    {
        Playing("spotify", "spotify:track:t1");
        await Target(BangerTargets.PerYear);
        Existing("spotify", Playlist("spotify", "other", "Road Trip"));
        CreateReturns("spotify", "pl-new", "Zorblax99's Stream Bangers 2026");
        PlaylistAcceptsTrack("spotify", "pl-new");

        Result<string> reply = await Sut().ExecuteAsync(Context());

        await _manage
            .Received(1)
            .CreatePlaylistAsync(
                Broadcaster,
                "spotify",
                Arg.Is<CreateMusicPlaylistDto>(d => d.Name == "Zorblax99's Stream Bangers 2026"),
                Arg.Any<CancellationToken>()
            );
        await _manage
            .Received(1)
            .AddPlaylistTracksAsync(
                Broadcaster,
                "spotify",
                "pl-new",
                Arg.Is<IReadOnlyList<string>>(u => u.SequenceEqual(new[] { "spotify:track:t1" })),
                Arg.Any<CancellationToken>()
            );
        await _manage
            .DidNotReceiveWithAnyArgs()
            .SaveTracksAsync(default, default!, default!, default);
        reply.Value.Should().Contain("Zorblax99's Stream Bangers 2026");
    }

    [Fact]
    public async Task Per_year_reuses_the_playlist_found_by_name_and_creates_nothing()
    {
        Playing("spotify", "spotify:track:t1");
        await Target(BangerTargets.PerYear);
        Existing(
            "spotify",
            Playlist("spotify", "pl-found", "Zorblax99's Stream Bangers 2026"),
            Playlist("spotify", "pl-last-year", "Zorblax99's Stream Bangers 2025")
        );
        PlaylistAcceptsTrack("spotify", "pl-found");

        await Sut().ExecuteAsync(Context());

        await _manage
            .DidNotReceiveWithAnyArgs()
            .CreatePlaylistAsync(default, default!, default!, default);
        await _manage
            .Received(1)
            .AddPlaylistTracksAsync(
                Broadcaster,
                "spotify",
                "pl-found",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task All_time_names_the_playlist_without_a_year_and_keeps_it_across_years()
    {
        Playing("spotify", "spotify:track:t1");
        await Target(BangerTargets.AllTime);
        Existing("spotify");
        CreateReturns("spotify", "pl-all", "Zorblax99's Stream Bangers");
        PlaylistAcceptsTrack("spotify", "pl-all");

        await Sut().ExecuteAsync(Context());

        await _manage
            .Received(1)
            .CreatePlaylistAsync(
                Broadcaster,
                "spotify",
                Arg.Is<CreateMusicPlaylistDto>(d => d.Name == "Zorblax99's Stream Bangers"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Per_year_on_a_youtube_song_creates_and_fills_the_playlist_on_youtube()
    {
        Playing("youtube", "youtube:video:abc123");
        await Target(BangerTargets.PerYear);
        Existing("youtube");
        CreateReturns("youtube", "yt-pl", "Zorblax99's Stream Bangers 2026");
        PlaylistAcceptsTrack("youtube", "yt-pl");

        await Sut().ExecuteAsync(Context());

        await _manage
            .Received(1)
            .CreatePlaylistAsync(
                Broadcaster,
                "youtube",
                Arg.Is<CreateMusicPlaylistDto>(d => d.Name == "Zorblax99's Stream Bangers 2026"),
                Arg.Any<CancellationToken>()
            );
        await _manage
            .Received(1)
            .AddPlaylistTracksAsync(
                Broadcaster,
                "youtube",
                "yt-pl",
                Arg.Is<IReadOnlyList<string>>(u =>
                    u.SequenceEqual(new[] { "youtube:video:abc123" })
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task The_year_comes_from_the_clock_so_next_year_starts_a_new_playlist()
    {
        Playing("spotify", "spotify:track:t1");
        await Target(BangerTargets.PerYear);
        Existing("spotify", Playlist("spotify", "pl-2026", "Zorblax99's Stream Bangers 2026"));
        CreateReturns("spotify", "pl-2027", "Zorblax99's Stream Bangers 2027");
        PlaylistAcceptsTrack("spotify", "pl-2027");
        _clock.SetUtcNow(new DateTimeOffset(2027, 1, 2, 9, 0, 0, TimeSpan.Zero));

        await Sut().ExecuteAsync(Context());

        await _manage
            .Received(1)
            .CreatePlaylistAsync(
                Broadcaster,
                "spotify",
                Arg.Is<CreateMusicPlaylistDto>(d => d.Name == "Zorblax99's Stream Bangers 2027"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task The_default_with_no_channel_playlist_saves_to_the_provider_likes()
    {
        Playing("youtube", "youtube:video:abc123");
        _manage
            .SaveTracksAsync(
                Broadcaster,
                "youtube",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        Result<string> reply = await Sut().ExecuteAsync(Context());

        await _manage
            .Received(1)
            .SaveTracksAsync(
                Broadcaster,
                "youtube",
                Arg.Is<IReadOnlyList<string>>(u =>
                    u.SequenceEqual(new[] { "youtube:video:abc123" })
                ),
                Arg.Any<CancellationToken>()
            );
        await _manage
            .DidNotReceiveWithAnyArgs()
            .CreatePlaylistAsync(default, default!, default!, default);
        reply.Value.Should().Contain(TrackName);
    }

    [Fact]
    public async Task A_fresh_channel_config_holds_the_default_target_and_the_choice_is_stored()
    {
        MusicConfigDto fresh = (await _config.Service.GetConfigAsync(Broadcaster.ToString())).Value;
        fresh.BangerTarget.Should().Be(BangerTargets.Default);

        await Target(BangerTargets.AllTime);

        MusicConfigDto saved = (await _config.Service.GetConfigAsync(Broadcaster.ToString())).Value;
        saved.BangerTarget.Should().Be(BangerTargets.AllTime);
    }
}
