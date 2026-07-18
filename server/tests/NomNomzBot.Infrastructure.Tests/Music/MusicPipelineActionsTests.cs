// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Music.Entities;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Music.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves the music parity pipeline actions do what their legacy commands did: <c>song_pause</c> /
/// <c>song_resume</c> / <c>song_previous</c> drive the corresponding transport member and propagate
/// its typed failure; <c>playlist_add</c> (!banger) appends the CURRENT track's exact URI to the
/// given playlist and fails typed on nothing-playing / no-provider; <c>song_wrong</c> (!wrongsong)
/// removes ONLY the caller's newest queued request; <c>song_ban</c> (!bansong) persists the block
/// row for the playing track AND skips it.
/// </summary>
public sealed class MusicPipelineActionsTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000ac001");

    private static PipelineExecutionContext Ctx(
        string userId = "twitch-42",
        string displayName = "Bamo"
    ) =>
        new()
        {
            BroadcasterId = ChannelId,
            TriggeredByUserId = userId,
            TriggeredByDisplayName = displayName,
            MessageId = "msg-1",
            RawMessage = "!cmd",
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

    private static NowPlaying Playing(
        string uri = "spotify:track:current",
        string name = "Current Song"
    ) => new(name, "Artist", "Album", null, 200_000, 10_000, true, 100, null, "spotify", uri);

    // ─── song_pause / song_resume / song_previous ─────────────────────────────

    [Fact]
    public async Task Song_pause_invokes_PauseAsync_and_succeeds()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .PauseAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        SongPauseAction action = new(music, NullLogger<SongPauseAction>.Instance);

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("song_pause"));

        result.Succeeded.Should().BeTrue();
        await music.Received(1).PauseAsync(ChannelId.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Song_pause_propagates_the_typed_failure()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .PauseAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("No active music provider.", "SERVICE_UNAVAILABLE"));
        SongPauseAction action = new(music, NullLogger<SongPauseAction>.Instance);

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("song_pause"));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("No active music provider.");
    }

    [Fact]
    public async Task Song_resume_invokes_PlayAsync_and_propagates_failure()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .PlayAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Spotify Premium required.", "PREMIUM_REQUIRED"));
        SongResumeAction action = new(music, NullLogger<SongResumeAction>.Instance);

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("song_resume"));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("Spotify Premium required.");
        await music.Received(1).PlayAsync(ChannelId.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Song_previous_invokes_PreviousAsync_and_succeeds()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .PreviousAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        SongPreviousAction action = new(music, NullLogger<SongPreviousAction>.Instance);

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("song_previous"));

        result.Succeeded.Should().BeTrue();
        await music.Received(1).PreviousAsync(ChannelId.ToString(), Arg.Any<CancellationToken>());
    }

    // ─── playlist_add (!banger) ───────────────────────────────────────────────

    [Fact]
    public async Task Playlist_add_appends_the_current_tracks_exact_uri_to_the_playlist()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetActiveProviderKeyAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns("spotify");
        music
            .GetNowPlayingAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Playing());
        IMusicProviderManageApi manage = Substitute.For<IMusicProviderManageApi>();
        manage
            .AddPlaylistTracksAsync(
                ChannelId,
                "spotify",
                "playlist-1",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        IChatProvider chat = Substitute.For<IChatProvider>();
        PlaylistAddAction action = new(music, manage, chat, NullLogger<PlaylistAddAction>.Instance);

        ActionResult result = await action.ExecuteAsync(
            Ctx(),
            Def("playlist_add", ("playlist_id", "playlist-1"))
        );

        result.Succeeded.Should().BeTrue();
        await manage
            .Received(1)
            .AddPlaylistTracksAsync(
                ChannelId,
                "spotify",
                "playlist-1",
                Arg.Is<IReadOnlyList<string>>(uris =>
                    uris.Count == 1 && uris[0] == "spotify:track:current"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Playlist_add_with_an_explicit_track_uri_passes_it_through()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetActiveProviderKeyAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns("spotify");
        IMusicProviderManageApi manage = Substitute.For<IMusicProviderManageApi>();
        manage
            .AddPlaylistTracksAsync(
                ChannelId,
                "spotify",
                "playlist-1",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        IChatProvider chat = Substitute.For<IChatProvider>();
        PlaylistAddAction action = new(music, manage, chat, NullLogger<PlaylistAddAction>.Instance);

        ActionResult result = await action.ExecuteAsync(
            Ctx(),
            Def("playlist_add", ("playlist_id", "playlist-1"), ("track_uri", "spotify:track:x9"))
        );

        result.Succeeded.Should().BeTrue();
        await music
            .DidNotReceive()
            .GetNowPlayingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await manage
            .Received(1)
            .AddPlaylistTracksAsync(
                ChannelId,
                "spotify",
                "playlist-1",
                Arg.Is<IReadOnlyList<string>>(uris =>
                    uris.Count == 1 && uris[0] == "spotify:track:x9"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Playlist_add_fails_typed_when_nothing_is_playing()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetActiveProviderKeyAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns("spotify");
        music
            .GetNowPlayingAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns((NowPlaying?)null);
        IMusicProviderManageApi manage = Substitute.For<IMusicProviderManageApi>();
        IChatProvider chat = Substitute.For<IChatProvider>();
        PlaylistAddAction action = new(music, manage, chat, NullLogger<PlaylistAddAction>.Instance);

        ActionResult result = await action.ExecuteAsync(
            Ctx(),
            Def("playlist_add", ("playlist_id", "playlist-1"))
        );

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("nothing is currently playing");
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
    public async Task Playlist_add_fails_typed_when_no_provider_is_active()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetActiveProviderKeyAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        IMusicProviderManageApi manage = Substitute.For<IMusicProviderManageApi>();
        IChatProvider chat = Substitute.For<IChatProvider>();
        PlaylistAddAction action = new(music, manage, chat, NullLogger<PlaylistAddAction>.Instance);

        ActionResult result = await action.ExecuteAsync(
            Ctx(),
            Def("playlist_add", ("playlist_id", "playlist-1"))
        );

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("no active music provider");
    }

    [Fact]
    public async Task Playlist_add_without_a_playlist_id_fails_typed()
    {
        IMusicService music = Substitute.For<IMusicService>();
        IMusicProviderManageApi manage = Substitute.For<IMusicProviderManageApi>();
        IChatProvider chat = Substitute.For<IChatProvider>();
        PlaylistAddAction action = new(music, manage, chat, NullLogger<PlaylistAddAction>.Instance);

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("playlist_add"));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("playlist_id");
    }

    // ─── song_wrong (!wrongsong) ──────────────────────────────────────────────

    [Fact]
    public async Task Song_wrong_removes_only_the_callers_newest_queued_request()
    {
        // Queue: [0] Bamo's first, [1] someone else's, [2] Bamo's newest → position 2 goes.
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetQueueAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                new MusicQueue(
                    null,
                    [
                        new MusicQueueItem("First Pick", "A", null, 100, "Bamo"),
                        new MusicQueueItem("Other Pick", "B", null, 100, "SomeoneElse"),
                        new MusicQueueItem("Newest Pick", "C", null, 100, "Bamo"),
                    ]
                )
            );
        music
            .RemoveFromQueueAsync(ChannelId.ToString(), 2, Arg.Any<CancellationToken>())
            .Returns(true);
        IChatProvider chat = Substitute.For<IChatProvider>();
        SongWrongAction action = new(music, chat, NullLogger<SongWrongAction>.Instance);

        ActionResult result = await action.ExecuteAsync(
            Ctx(displayName: "Bamo"),
            Def("song_wrong")
        );

        result.Succeeded.Should().BeTrue();
        result.Output.Should().Be("removed: Newest Pick");
        await music
            .Received(1)
            .RemoveFromQueueAsync(ChannelId.ToString(), 2, Arg.Any<CancellationToken>());
        // Nobody else's request was touched.
        await music
            .DidNotReceive()
            .RemoveFromQueueAsync(
                ChannelId.ToString(),
                Arg.Is<int>(p => p != 2),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Song_wrong_fails_typed_when_the_caller_has_no_queued_request()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetQueueAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                new MusicQueue(
                    null,
                    [new MusicQueueItem("Other Pick", "B", null, 100, "SomeoneElse")]
                )
            );
        IChatProvider chat = Substitute.For<IChatProvider>();
        SongWrongAction action = new(music, chat, NullLogger<SongWrongAction>.Instance);

        ActionResult result = await action.ExecuteAsync(
            Ctx(displayName: "Bamo"),
            Def("song_wrong")
        );

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("no queued request for the triggering user");
        await music
            .DidNotReceive()
            .RemoveFromQueueAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ─── song_ban (!bansong) ──────────────────────────────────────────────────

    [Fact]
    public async Task Song_ban_blocks_the_playing_track_with_the_full_row_shape_and_skips()
    {
        MusicTestDbContext db = new(
            new DbContextOptionsBuilder<MusicTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        BlockedTrackService blocks = new(db);
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Playing(uri: "spotify:track:bad", name: "Bad Song"));
        music
            .SkipAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        IChatProvider chat = Substitute.For<IChatProvider>();
        SongBanAction action = new(music, blocks, chat, NullLogger<SongBanAction>.Instance);

        ActionResult result = await action.ExecuteAsync(
            Ctx(userId: "twitch-42"),
            Def("song_ban", ("reason", "not stream-safe"))
        );

        result.Succeeded.Should().BeTrue();

        // The block row landed with its full shape…
        BlockedTrack row = db.BlockedTracks.Single();
        row.BroadcasterId.Should().Be(ChannelId);
        row.Provider.Should().Be("spotify");
        row.TrackUri.Should().Be("spotify:track:bad");
        row.Title.Should().Be("Bad Song");
        row.Reason.Should().Be("not stream-safe");
        row.BlockedByUserId.Should().Be("twitch-42");
        (await blocks.IsBlockedAsync(ChannelId, "spotify:track:bad")).Should().BeTrue();

        // …AND the playing track was skipped.
        await music.Received(1).SkipAsync(ChannelId.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Song_ban_fails_typed_when_nothing_is_playing_and_neither_blocks_nor_skips()
    {
        MusicTestDbContext db = new(
            new DbContextOptionsBuilder<MusicTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        BlockedTrackService blocks = new(db);
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns((NowPlaying?)null);
        IChatProvider chat = Substitute.For<IChatProvider>();
        SongBanAction action = new(music, blocks, chat, NullLogger<SongBanAction>.Instance);

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("song_ban"));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("nothing is currently playing");
        db.BlockedTracks.Should().BeEmpty();
        await music.DidNotReceive().SkipAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
