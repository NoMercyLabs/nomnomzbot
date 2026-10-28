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

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Music.Pipeline;
using NomNomzBot.Infrastructure.Music.PipelineActions;
using NSubstitute;
using MusicPlaylistDto = NomNomzBot.Application.Contracts.Music.MusicPlaylistDto;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// A playlist step carries an optional <c>provider</c> (spotify | youtube). Absent means the channel's active
/// provider, so a step saved before the setting existed (<c>{"type":"playlist_add","playlist_id":"..."}</c>)
/// keeps doing what it did, with no migration.
/// </summary>
public sealed class PlaylistProviderChoiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000ac003");

    private static PipelineExecutionContext Ctx() =>
        new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "twitch-42",
            TriggeredByDisplayName = "Bamo",
            MessageId = "msg-1",
            RawMessage = "!banger",
        };

    private static ActionDefinition Def(
        string type,
        params (string Key, string Value)[] parameters
    ) =>
        new()
        {
            Type = type,
            Parameters = parameters.ToDictionary(
                p => p.Key,
                p => JsonSerializer.SerializeToElement(p.Value)
            ),
        };

    private static NowPlaying Playing(string provider, string uri) =>
        new("Song", "Artist", null, null, 200_000, 1_000, true, 100, null, provider, uri);

    private static IMusicService MusicWith(string? active, NowPlaying? playing)
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetActiveProviderKeyAsync(Channel.ToString(), Arg.Any<CancellationToken>())
            .Returns(active);
        music.GetNowPlayingAsync(Channel.ToString(), Arg.Any<CancellationToken>()).Returns(playing);
        return music;
    }

    private static IMusicProviderManageApi ManageThatAccepts()
    {
        IMusicProviderManageApi manage = Substitute.For<IMusicProviderManageApi>();
        manage
            .AddPlaylistTracksAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        manage
            .RemovePlaylistTracksAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        manage
            .IsTrackInPlaylistAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(false));
        manage
            .ListPlaylistsAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<MusicPlaylistDto>>([]));
        return manage;
    }

    private static PlaylistAddAction NewAdd(IMusicService music, IMusicProviderManageApi manage) =>
        new(
            music,
            manage,
            Substitute.For<IChatProvider>(),
            Substitute.For<ITemplateResolver>(),
            Substitute.For<IBuiltinResponseComposer>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<PlaylistAddAction>.Instance
        );

    private static Task ReceivedAddOn(
        IMusicProviderManageApi manage,
        string provider,
        string uri
    ) =>
        manage
            .Received(1)
            .AddPlaylistTracksAsync(
                Channel,
                provider,
                "pl-1",
                Arg.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == uri),
                Arg.Any<CancellationToken>()
            );

    [Fact]
    public async Task Playlist_add_without_a_provider_uses_the_active_provider()
    {
        IMusicProviderManageApi manage = ManageThatAccepts();
        PlaylistAddAction action = NewAdd(
            MusicWith("spotify", Playing("spotify", "spotify:track:a")),
            manage
        );

        ActionResult result = await action.ExecuteAsync(
            Ctx(),
            Def("playlist_add", ("playlist_id", "pl-1"))
        );

        result.Succeeded.Should().BeTrue();
        await ReceivedAddOn(manage, "spotify", "spotify:track:a");
    }

    [Fact]
    public async Task Playlist_add_with_provider_youtube_calls_the_youtube_api_even_when_spotify_is_active()
    {
        IMusicProviderManageApi manage = ManageThatAccepts();
        PlaylistAddAction action = NewAdd(
            MusicWith("spotify", Playing("youtube", "yt-track")),
            manage
        );

        ActionResult result = await action.ExecuteAsync(
            Ctx(),
            Def("playlist_add", ("playlist_id", "pl-1"), ("provider", "youtube"))
        );

        result.Succeeded.Should().BeTrue();
        await ReceivedAddOn(manage, "youtube", "yt-track");
        await manage
            .DidNotReceive()
            .AddPlaylistTracksAsync(
                Arg.Any<Guid>(),
                "spotify",
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Playlist_add_fails_clearly_when_the_playing_track_is_from_the_other_service()
    {
        IMusicProviderManageApi manage = ManageThatAccepts();
        PlaylistAddAction action = NewAdd(
            MusicWith("spotify", Playing("spotify", "spotify:track:a")),
            manage
        );

        ActionResult result = await action.ExecuteAsync(
            Ctx(),
            Def("playlist_add", ("playlist_id", "pl-1"), ("provider", "youtube"))
        );

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("from spotify, not youtube");
        await manage
            .DidNotReceive()
            .AddPlaylistTracksAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Playlist_add_with_a_given_track_uri_adds_it_to_the_chosen_service()
    {
        IMusicProviderManageApi manage = ManageThatAccepts();
        PlaylistAddAction action = NewAdd(MusicWith("spotify", null), manage);

        ActionResult result = await action.ExecuteAsync(
            Ctx(),
            Def(
                "playlist_add",
                ("playlist_id", "pl-1"),
                ("provider", "youtube"),
                ("track_uri", "yt-given")
            )
        );

        result.Succeeded.Should().BeTrue();
        await ReceivedAddOn(manage, "youtube", "yt-given");
    }

    [Fact]
    public async Task Playlist_add_rejects_an_unknown_provider()
    {
        IMusicProviderManageApi manage = ManageThatAccepts();
        PlaylistAddAction action = NewAdd(MusicWith("spotify", Playing("spotify", "x")), manage);

        ActionResult result = await action.ExecuteAsync(
            Ctx(),
            Def("playlist_add", ("playlist_id", "pl-1"), ("provider", "tidal"))
        );

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("unknown music provider");
    }

    [Fact]
    public async Task Add_to_playlist_without_a_provider_uses_the_active_provider_and_youtube_when_chosen()
    {
        IMusicProviderManageApi manage = ManageThatAccepts();
        MusicAddToPlaylistAction absent = new(
            MusicWith("youtube", Playing("youtube", "yt-1")),
            manage
        );
        MusicAddToPlaylistAction chosen = new(
            MusicWith("spotify", Playing("youtube", "yt-2")),
            manage
        );

        ActionResult first = await absent.ExecuteAsync(
            Ctx(),
            Def("music_add_to_playlist", ("playlistId", "pl-1"))
        );
        ActionResult second = await chosen.ExecuteAsync(
            Ctx(),
            Def("music_add_to_playlist", ("playlistId", "pl-1"), ("provider", "youtube"))
        );

        first.Succeeded.Should().BeTrue();
        second.Succeeded.Should().BeTrue();
        await ReceivedAddOn(manage, "youtube", "yt-1");
        await ReceivedAddOn(manage, "youtube", "yt-2");
    }

    [Fact]
    public async Task Remove_from_playlist_fails_on_a_cross_service_track_and_removes_on_the_chosen_service()
    {
        IMusicProviderManageApi manage = ManageThatAccepts();
        MusicRemoveFromPlaylistAction mismatch = new(
            MusicWith("spotify", Playing("spotify", "spotify:track:a")),
            manage
        );
        MusicRemoveFromPlaylistAction match = new(
            MusicWith("spotify", Playing("youtube", "yt-3")),
            manage
        );

        ActionResult failed = await mismatch.ExecuteAsync(
            Ctx(),
            Def("music_remove_from_playlist", ("playlistId", "pl-1"), ("provider", "youtube"))
        );
        ActionResult removed = await match.ExecuteAsync(
            Ctx(),
            Def("music_remove_from_playlist", ("playlistId", "pl-1"), ("provider", "youtube"))
        );

        failed.Succeeded.Should().BeFalse();
        failed.ErrorMessage.Should().Contain("from spotify, not youtube");
        removed.Succeeded.Should().BeTrue();
        await manage
            .Received(1)
            .RemovePlaylistTracksAsync(
                Channel,
                "youtube",
                "pl-1",
                Arg.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == "yt-3"),
                Arg.Any<CancellationToken>()
            );
    }

    private static PipelineOptionQuery Query(string? provider) =>
        new(
            null,
            null,
            new PaginationParams(1, 25, null, null),
            provider is null ? null : new Dictionary<string, string> { ["provider"] = provider }
        );

    [Fact]
    public async Task Picker_lists_only_the_chosen_providers_playlists()
    {
        IMusicProviderManageApi manage = ManageThatAccepts();
        manage
            .ListPlaylistsAsync(Channel, "youtube", Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<MusicPlaylistDto>>([
                    new MusicPlaylistDto("yt-pl", "YT list", null, false, 3, null, "youtube"),
                ])
            );
        MusicPlaylistOptionProvider picker = new(MusicWith("spotify", null), manage);

        Result<PipelineOptionListResult> listed = await picker.GetOptionsAsync(
            Channel,
            Query("youtube")
        );

        PipelineOption option = listed.Value.Items.Should().ContainSingle().Subject;
        option.Value.Should().Be("yt-pl");
        await manage
            .DidNotReceive()
            .ListPlaylistsAsync(Channel, "spotify", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Picker_without_a_provider_lists_the_active_providers_playlists()
    {
        IMusicProviderManageApi manage = ManageThatAccepts();
        MusicPlaylistOptionProvider picker = new(MusicWith("spotify", null), manage);

        Result<PipelineOptionListResult> listed = await picker.GetOptionsAsync(
            Channel,
            Query(null)
        );

        listed.Value.SourceAvailable.Should().BeTrue();
        await manage
            .Received()
            .ListPlaylistsAsync(Channel, "spotify", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Picker_is_unavailable_with_the_reason_when_the_chosen_service_is_not_connected()
    {
        IMusicProviderManageApi manage = ManageThatAccepts();
        manage
            .ListPlaylistsAsync(Channel, "youtube", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<MusicPlaylistDto>>("YouTube is not connected."));
        MusicPlaylistOptionProvider picker = new(MusicWith("spotify", null), manage);

        Result<PipelineOptionListResult> listed = await picker.GetOptionsAsync(
            Channel,
            Query("youtube")
        );

        listed.Value.SourceAvailable.Should().BeFalse();
        listed.Value.UnavailableReason.Should().Be("YouTube is not connected.");
    }

    [Fact]
    public void The_three_playlist_actions_declare_the_provider_choice_with_both_services()
    {
        IEnumerable<ICommandAction> actions =
        [
            NewAdd(MusicWith(null, null), ManageThatAccepts()),
            new MusicAddToPlaylistAction(MusicWith(null, null), ManageThatAccepts()),
            new MusicRemoveFromPlaylistAction(MusicWith(null, null), ManageThatAccepts()),
        ];

        foreach (ICommandAction action in actions)
        {
            PipelineActionFieldDescriptor field = action.Fields.Single(f => f.Name == "provider");
            field.Kind.Should().Be(PipelineActionFieldKind.Enum);
            field.Required.Should().BeFalse();
            field.Options.Should().Equal("spotify", "youtube");
        }
    }
}
