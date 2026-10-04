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
using NomNomzBot.Application.Commands.Builtin.Personality;
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
/// <c>!banger</c> adds the playing track to the channel's playlist: its own, or one made on first use. A
/// track that is already there is never added twice and gets the snarky reply.
/// </summary>
public sealed class BangerBuiltinTests : IDisposable
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-000000009903");
    private const string TrackUri = "spotify:track:rick1";
    private const string TrackName = "Never Gonna Give You Up";

    private readonly MusicConfigDbFixture _config = new();
    private readonly IMusicService _music = Substitute.For<IMusicService>();
    private readonly IMusicProviderManageApi _playlists = Substitute.For<IMusicProviderManageApi>();

    public void Dispose() => _config.Dispose();

    private BangerBuiltin Sut() => new(_music, _playlists, _config.Service, FakeComposer());

    private static BuiltinCommandContext Context() =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeringUserId = "viewer-1",
            TriggeringUserDisplayName = "Viewer1",
            RoleLevel = 0,
            Personality = PersonalityTone.Sassy,
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

    private void Playing() =>
        _music
            .GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                new NowPlaying(
                    TrackName: TrackName,
                    Artist: "Rick Astley",
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

    private static List<string> Texts(string slot) =>
        [
            .. ToneTemplateCatalog
                .Get(PersonalityTone.Sassy, BuiltinResponseSlots.Banger.Key, slot)
                .Select(t => t.Replace("{user}", "Viewer1").Replace("{track.name}", TrackName)),
        ];

    [Fact]
    public async Task A_new_track_is_added_to_the_configured_playlist_once()
    {
        Playing();
        await ChoosePlaylist();
        TrackPresence("pl1", Result.Success(false));
        AddSucceeds("pl1");

        Result<string> reply = await Sut().ExecuteAsync(Context());

        await AddReceived(1, "pl1");
        Texts(BuiltinResponseSlots.Banger.Added).Should().Contain(reply.Value);
        reply.Value.Should().Contain(TrackName);
    }

    [Fact]
    public async Task A_track_already_in_the_playlist_is_not_added_and_gets_a_snarky_reply_with_the_name()
    {
        Playing();
        await ChoosePlaylist();
        TrackPresence("pl1", Result.Success(true));

        Result<string> reply = await Sut().ExecuteAsync(Context());

        await AddReceived(0, "pl1");
        Texts(BuiltinResponseSlots.Banger.AlreadyThere)
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
        Texts(BuiltinResponseSlots.Banger.Added).Should().Contain(reply.Value);
    }

    [Fact]
    public async Task With_no_playlist_and_auto_create_off_the_provider_is_never_called_and_the_reply_asks_for_a_playlist()
    {
        Playing();

        Result<string> reply = await Sut().ExecuteAsync(Context());

        _playlists.ReceivedCalls().Should().BeEmpty();
        Texts(BuiltinResponseSlots.Banger.NoPlaylist).Should().Contain(reply.Value);
    }

    [Fact]
    public async Task With_nothing_playing_the_provider_is_never_called_and_the_reply_says_so()
    {
        _music
            .GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns((NowPlaying?)null);

        Result<string> reply = await Sut().ExecuteAsync(Context());

        _playlists.ReceivedCalls().Should().BeEmpty();
        Texts(BuiltinResponseSlots.Banger.Nothing).Should().Contain(reply.Value);
    }

    [Fact]
    public async Task A_provider_failure_gets_the_failed_reply_and_no_add()
    {
        Playing();
        await ChoosePlaylist();
        TrackPresence("pl1", Result.Failure<bool>("no scope", "MISSING_SCOPE"));

        Result<string> reply = await Sut().ExecuteAsync(Context());

        await AddReceived(0, "pl1");
        Texts(BuiltinResponseSlots.Banger.Failed).Should().Contain(reply.Value);
    }
}
