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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Music.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves the silent music-control automation actions (music-automation-controls.md §3.1) call the
/// right seam member with the right arguments — and, for the toggle/cycle actions, write the OPPOSITE
/// of a fixed current-state fixture, not just "some write happened".
/// </summary>
public sealed class MusicControlActionsTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000ac002");

    private static PipelineExecutionContext Ctx() =>
        new()
        {
            BroadcasterId = ChannelId,
            TriggeredByUserId = "twitch-42",
            TriggeredByDisplayName = "Bamo",
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
        bool isPlaying = true,
        bool shuffleEnabled = false,
        MusicRepeatMode repeatMode = MusicRepeatMode.Off,
        string? trackUri = "spotify:track:current",
        string? artistId = "spotify:artist:1"
    ) =>
        new(
            "Current Song",
            "Artist",
            "Album",
            null,
            200_000,
            10_000,
            isPlaying,
            100,
            null,
            "spotify",
            trackUri,
            shuffleEnabled,
            repeatMode,
            artistId
        );

    // ─── music_play_pause: toggle asserts the OPPOSITE of the fixture ─────────

    [Fact]
    public async Task Play_pause_pauses_when_currently_playing()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Playing(isPlaying: true));
        music
            .PauseAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        MusicPlayPauseAction action = new(music);

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("music_play_pause"));

        result.Succeeded.Should().BeTrue();
        await music.Received(1).PauseAsync(ChannelId.ToString(), Arg.Any<CancellationToken>());
        await music.DidNotReceive().PlayAsync(ChannelId.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Play_pause_plays_when_currently_paused()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Playing(isPlaying: false));
        music
            .PlayAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        MusicPlayPauseAction action = new(music);

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("music_play_pause"));

        result.Succeeded.Should().BeTrue();
        await music.Received(1).PlayAsync(ChannelId.ToString(), Arg.Any<CancellationToken>());
        await music.DidNotReceive().PauseAsync(ChannelId.ToString(), Arg.Any<CancellationToken>());
    }

    /// <summary>The latency fix: when a cached hint is available, the toggle must decide from THAT and
    /// never call the full <c>GetNowPlayingAsync</c> read at all — a redundant live provider round trip
    /// on every single toggle press otherwise. A test that only checked Pause/Play were called would
    /// pass even if this were still calling GetNowPlayingAsync every time — DidNotReceive on the read
    /// itself is the assertion that actually pins the fast path.</summary>
    [Fact]
    public async Task Play_pause_decides_from_the_cached_hint_without_a_full_read_when_available()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .TryGetCachedIsPlayingAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns((bool?)true);
        music
            .PauseAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        MusicPlayPauseAction action = new(music);

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("music_play_pause"));

        result.Succeeded.Should().BeTrue();
        await music.Received(1).PauseAsync(ChannelId.ToString(), Arg.Any<CancellationToken>());
        await music
            .DidNotReceive()
            .GetNowPlayingAsync(ChannelId.ToString(), Arg.Any<CancellationToken>());
    }

    // ─── music_toggle_shuffle: writes the OPPOSITE of the fixture ─────────────

    [Fact]
    public async Task Toggle_shuffle_turns_shuffle_off_when_currently_on()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Playing(shuffleEnabled: true));
        music
            .SetShuffleAsync(ChannelId.ToString(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        MusicToggleShuffleAction action = new(music);

        await action.ExecuteAsync(Ctx(), Def("music_toggle_shuffle"));

        await music
            .Received(1)
            .SetShuffleAsync(ChannelId.ToString(), false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Toggle_shuffle_turns_shuffle_on_when_currently_off()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Playing(shuffleEnabled: false));
        music
            .SetShuffleAsync(ChannelId.ToString(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        MusicToggleShuffleAction action = new(music);

        await action.ExecuteAsync(Ctx(), Def("music_toggle_shuffle"));

        await music
            .Received(1)
            .SetShuffleAsync(ChannelId.ToString(), true, Arg.Any<CancellationToken>());
    }

    // ─── music_cycle_repeat: advances Off -> Track -> Context -> Off ──────────

    [Theory]
    [InlineData(MusicRepeatMode.Off, "track")]
    [InlineData(MusicRepeatMode.Track, "context")]
    [InlineData(MusicRepeatMode.Context, "off")]
    public async Task Cycle_repeat_advances_to_the_next_mode(
        MusicRepeatMode current,
        string expectedNext
    )
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Playing(repeatMode: current));
        music
            .SetRepeatAsync(ChannelId.ToString(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        MusicCycleRepeatAction action = new(music);

        await action.ExecuteAsync(Ctx(), Def("music_cycle_repeat"));

        await music
            .Received(1)
            .SetRepeatAsync(ChannelId.ToString(), expectedNext, Arg.Any<CancellationToken>());
    }

    // ─── music_toggle_saved: writes the OPPOSITE of the saved-check fixture ───

    [Fact]
    public async Task Toggle_saved_removes_when_already_saved()
    {
        IMusicService music = Substitute.For<IMusicService>();
        IMusicProviderManageApi manageApi = Substitute.For<IMusicProviderManageApi>();
        music
            .GetActiveProviderKeyAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns("spotify");
        music
            .GetNowPlayingAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Playing());
        manageApi
            .AreTracksSavedAsync(
                ChannelId,
                "spotify",
                Arg.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == "spotify:track:current"),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<bool>>([true]));
        manageApi
            .RemoveSavedTracksAsync(
                ChannelId,
                "spotify",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        IEventBus eventBus = Substitute.For<IEventBus>();
        MusicToggleSavedAction action = new(music, manageApi, eventBus);

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("music_toggle_saved"));

        result.Succeeded.Should().BeTrue();
        await manageApi
            .Received(1)
            .RemoveSavedTracksAsync(
                ChannelId,
                "spotify",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            );
        await manageApi
            .DidNotReceive()
            .SaveTracksAsync(
                ChannelId,
                "spotify",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            );
        // Was saved, toggled off — the overlay heart animation reacts to isSaved:false.
        await eventBus
            .Received(1)
            .PublishAsync(
                Arg.Is<TrackSavedChangedEvent>(e => e.IsSaved == false),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Toggle_saved_saves_when_not_yet_saved()
    {
        IMusicService music = Substitute.For<IMusicService>();
        IMusicProviderManageApi manageApi = Substitute.For<IMusicProviderManageApi>();
        music
            .GetActiveProviderKeyAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns("spotify");
        music
            .GetNowPlayingAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Playing());
        manageApi
            .AreTracksSavedAsync(
                ChannelId,
                "spotify",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<bool>>([false]));
        manageApi
            .SaveTracksAsync(
                ChannelId,
                "spotify",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        IEventBus eventBus = Substitute.For<IEventBus>();
        MusicToggleSavedAction action = new(music, manageApi, eventBus);

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("music_toggle_saved"));

        result.Succeeded.Should().BeTrue();
        await manageApi
            .Received(1)
            .SaveTracksAsync(
                ChannelId,
                "spotify",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            );
        await manageApi
            .DidNotReceive()
            .RemoveSavedTracksAsync(
                ChannelId,
                "spotify",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    // ─── capability-absent: no provider connected fails typed, never calls the seam ───

    [Fact]
    public async Task Save_track_fails_CAPABILITY_UNSUPPORTED_when_no_provider_connected()
    {
        IMusicService music = Substitute.For<IMusicService>();
        IMusicProviderManageApi manageApi = Substitute.For<IMusicProviderManageApi>();
        music
            .GetActiveProviderKeyAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        MusicSaveTrackAction action = new(music, manageApi, Substitute.For<IEventBus>());

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("music_save_track"));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("CAPABILITY_UNSUPPORTED");
        await manageApi
            .DidNotReceive()
            .SaveTracksAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Follow_artist_fails_CAPABILITY_UNSUPPORTED_when_artist_id_unknown()
    {
        IMusicService music = Substitute.For<IMusicService>();
        IMusicProviderManageApi manageApi = Substitute.For<IMusicProviderManageApi>();
        music
            .GetActiveProviderKeyAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns("spotify");
        music
            .GetNowPlayingAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Playing(artistId: null));
        MusicFollowArtistAction action = new(music, manageApi);

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("music_follow_artist"));

        result.Succeeded.Should().BeFalse();
        await manageApi
            .DidNotReceive()
            .FollowAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<MusicFollowTarget>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Follow_artist_follows_the_current_tracks_artist()
    {
        IMusicService music = Substitute.For<IMusicService>();
        IMusicProviderManageApi manageApi = Substitute.For<IMusicProviderManageApi>();
        music
            .GetActiveProviderKeyAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns("spotify");
        music
            .GetNowPlayingAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Playing(artistId: "spotify:artist:99"));
        manageApi
            .FollowAsync(
                ChannelId,
                "spotify",
                MusicFollowTarget.Artist,
                "spotify:artist:99",
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        MusicFollowArtistAction action = new(music, manageApi);

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("music_follow_artist"));

        result.Succeeded.Should().BeTrue();
        await manageApi
            .Received(1)
            .FollowAsync(
                ChannelId,
                "spotify",
                MusicFollowTarget.Artist,
                "spotify:artist:99",
                Arg.Any<CancellationToken>()
            );
    }

    // ─── music_set_volume: resolves the numeric param and calls SetVolumeAsync ────

    [Fact]
    public async Task Set_volume_calls_SetVolumeAsync_with_the_resolved_value()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .SetVolumeAsync(ChannelId.ToString(), 42, Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        MusicSetVolumeAction action = new(music);

        ActionResult result = await action.ExecuteAsync(
            Ctx(),
            Def("music_set_volume", ("volume", "42"))
        );

        result.Succeeded.Should().BeTrue();
        await music
            .Received(1)
            .SetVolumeAsync(ChannelId.ToString(), 42, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Set_volume_propagates_typed_failure()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .SetVolumeAsync(ChannelId.ToString(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("volume unsupported", "CAPABILITY_UNSUPPORTED"));
        MusicSetVolumeAction action = new(music);

        ActionResult result = await action.ExecuteAsync(
            Ctx(),
            Def("music_set_volume", ("volume", "50"))
        );

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("CAPABILITY_UNSUPPORTED");
    }

    // ─── music_add_to_playlist: appends the current track to the given playlist ──

    [Fact]
    public async Task Add_to_playlist_appends_the_current_tracks_uri()
    {
        IMusicService music = Substitute.For<IMusicService>();
        IMusicProviderManageApi manageApi = Substitute.For<IMusicProviderManageApi>();
        music
            .GetActiveProviderKeyAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns("spotify");
        music
            .GetNowPlayingAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Playing());
        manageApi
            .AddPlaylistTracksAsync(
                ChannelId,
                "spotify",
                "playlist-1",
                Arg.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == "spotify:track:current"),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        MusicAddToPlaylistAction action = new(music, manageApi);

        ActionResult result = await action.ExecuteAsync(
            Ctx(),
            Def("music_add_to_playlist", ("playlistId", "playlist-1"))
        );

        result.Succeeded.Should().BeTrue();
        await manageApi
            .Received(1)
            .AddPlaylistTracksAsync(
                ChannelId,
                "spotify",
                "playlist-1",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Add_to_playlist_fails_without_a_playlistId_param()
    {
        IMusicService music = Substitute.For<IMusicService>();
        IMusicProviderManageApi manageApi = Substitute.For<IMusicProviderManageApi>();
        MusicAddToPlaylistAction action = new(music, manageApi);

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("music_add_to_playlist"));

        result.Succeeded.Should().BeFalse();
        await manageApi
            .DidNotReceive()
            .AddPlaylistTracksAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    // ─── music_volume_up / music_volume_down / music_volume_mute ──────────────

    private static MusicDeviceDto ActiveDevice(int volumePercent) =>
        new("dev-1", "Laptop", "Computer", true, volumePercent);

    [Fact]
    public async Task Volume_up_steps_from_the_devices_real_current_volume()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetDevicesAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MusicDeviceDto>)[ActiveDevice(40)]);
        music
            .SetVolumeAsync(ChannelId.ToString(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        MusicVolumeUpAction action = new(music);

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("music_volume_up"));

        result.Succeeded.Should().BeTrue();
        await music
            .Received(1)
            .SetVolumeAsync(ChannelId.ToString(), 50, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Volume_up_clamps_at_100()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetDevicesAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MusicDeviceDto>)[ActiveDevice(95)]);
        music
            .SetVolumeAsync(ChannelId.ToString(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        MusicVolumeUpAction action = new(music);

        await action.ExecuteAsync(Ctx(), Def("music_volume_up"));

        await music
            .Received(1)
            .SetVolumeAsync(ChannelId.ToString(), 100, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Volume_up_respects_a_custom_step_param()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetDevicesAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MusicDeviceDto>)[ActiveDevice(20)]);
        music
            .SetVolumeAsync(ChannelId.ToString(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        MusicVolumeUpAction action = new(music);

        await action.ExecuteAsync(Ctx(), Def("music_volume_up", ("step", "25")));

        await music
            .Received(1)
            .SetVolumeAsync(ChannelId.ToString(), 45, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Volume_down_steps_from_the_devices_real_current_volume()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetDevicesAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MusicDeviceDto>)[ActiveDevice(40)]);
        music
            .SetVolumeAsync(ChannelId.ToString(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        MusicVolumeDownAction action = new(music);

        await action.ExecuteAsync(Ctx(), Def("music_volume_down"));

        await music
            .Received(1)
            .SetVolumeAsync(ChannelId.ToString(), 30, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Volume_down_clamps_at_0()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetDevicesAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MusicDeviceDto>)[ActiveDevice(5)]);
        music
            .SetVolumeAsync(ChannelId.ToString(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        MusicVolumeDownAction action = new(music);

        await action.ExecuteAsync(Ctx(), Def("music_volume_down"));

        await music
            .Received(1)
            .SetVolumeAsync(ChannelId.ToString(), 0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Volume_mute_mutes_when_audible_and_remembers_the_real_level()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetDevicesAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MusicDeviceDto>)[ActiveDevice(60)]);
        music
            .SetVolumeAsync(ChannelId.ToString(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        IMuteVolumeMemory memory = Substitute.For<IMuteVolumeMemory>();
        MusicVolumeMuteAction action = new(music, memory);

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("music_volume_mute"));

        result.Succeeded.Should().BeTrue();
        await music
            .Received(1)
            .SetVolumeAsync(ChannelId.ToString(), 0, Arg.Any<CancellationToken>());
        // The pre-mute level (60, not the fixed unmute default) is what gets remembered for later.
        await memory.Received(1).RememberAsync(ChannelId, 60, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Volume_unmute_restores_the_remembered_pre_mute_level()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetDevicesAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MusicDeviceDto>)[ActiveDevice(0)]);
        music
            .SetVolumeAsync(ChannelId.ToString(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        IMuteVolumeMemory memory = Substitute.For<IMuteVolumeMemory>();
        memory.GetAsync(ChannelId, Arg.Any<CancellationToken>()).Returns((int?)73);
        MusicVolumeMuteAction action = new(music, memory);

        await action.ExecuteAsync(Ctx(), Def("music_volume_mute"));

        // Restores the real remembered level, NOT the fixed 50 default — the whole point of remembering it.
        await music
            .Received(1)
            .SetVolumeAsync(ChannelId.ToString(), 73, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Volume_mute_unmutes_to_the_default_level_when_nothing_is_remembered()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetDevicesAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MusicDeviceDto>)[ActiveDevice(0)]);
        music
            .SetVolumeAsync(ChannelId.ToString(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        IMuteVolumeMemory memory = Substitute.For<IMuteVolumeMemory>();
        memory.GetAsync(ChannelId, Arg.Any<CancellationToken>()).Returns((int?)null);
        MusicVolumeMuteAction action = new(music, memory);

        await action.ExecuteAsync(Ctx(), Def("music_volume_mute"));

        await music
            .Received(1)
            .SetVolumeAsync(ChannelId.ToString(), 50, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Volume_mute_unmutes_to_a_custom_level_param_when_nothing_is_remembered()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetDevicesAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MusicDeviceDto>)[ActiveDevice(0)]);
        music
            .SetVolumeAsync(ChannelId.ToString(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        IMuteVolumeMemory memory = Substitute.For<IMuteVolumeMemory>();
        memory.GetAsync(ChannelId, Arg.Any<CancellationToken>()).Returns((int?)null);
        MusicVolumeMuteAction action = new(music, memory);

        await action.ExecuteAsync(Ctx(), Def("music_volume_mute", ("unmuteVolume", "80")));

        await music
            .Received(1)
            .SetVolumeAsync(ChannelId.ToString(), 80, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Volume_up_fails_CAPABILITY_UNSUPPORTED_when_no_device_is_connected()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetDevicesAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MusicDeviceDto>)[]);
        MusicVolumeUpAction action = new(music);

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("music_volume_up"));

        result.Succeeded.Should().BeFalse();
        await music
            .DidNotReceive()
            .SetVolumeAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
