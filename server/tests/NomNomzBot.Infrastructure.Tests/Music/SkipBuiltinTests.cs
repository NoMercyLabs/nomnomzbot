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
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves S-PL8: <c>!skip N</c> is a VIEWER self-service action that removes the Nth of the CALLING
/// user's own queued requests — it must never touch the currently-playing track and must never let a
/// caller remove a request that isn't theirs. Bare <c>!skip</c> (no argument) keeps its pre-existing
/// moderator-gated "skip whatever is playing now" behavior, unchanged (regression guard).
/// </summary>
public sealed class SkipBuiltinTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-0000000ac002");

    private const int ViewerLevel = 0;
    private const int ModeratorLevel = 10;

    private static BuiltinCommandContext Ctx(
        string args,
        int roleLevel,
        string displayName = "Bamo"
    ) =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeringUserId = "twitch-42",
            TriggeringUserDisplayName = displayName,
            RoleLevel = roleLevel,
            Args = args,
        };

    private static MusicQueueItem Item(string trackName, string? requestedBy) =>
        new(
            TrackName: trackName,
            Artist: "Artist",
            ImageUrl: null,
            DurationMs: 180_000,
            RequestedBy: requestedBy
        );

    [Fact]
    public async Task Skip_N_removes_the_callers_own_Nth_queued_request_and_never_touches_playback()
    {
        // Position-ordered queue mixing other viewers' requests with the caller's two own requests —
        // "Second Song" is the caller's SECOND own entry even though it sits at global index 3.
        MusicQueueItem first = Item("Other Song A", "SomeoneElse");
        MusicQueueItem callersFirst = Item("First Song", "Bamo");
        MusicQueueItem other = Item("Other Song B", "SomeoneElse");
        MusicQueueItem callersSecond = Item("Second Song", "Bamo");
        MusicQueue queue = new(
            CurrentTrack: null,
            Queue: [first, callersFirst, other, callersSecond]
        );

        IMusicService music = Substitute.For<IMusicService>();
        music.GetQueueAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>()).Returns(queue);
        music
            .RemoveFromQueueAsync(Broadcaster.ToString(), 3, Arg.Any<CancellationToken>())
            .Returns(true);
        SkipBuiltin sut = new(
            music,
            TestBuiltinComposer.Create(),
            MusicGateTestKit.Gate(holdsGrant: false)
        );

        Result<string> result = await sut.ExecuteAsync(Ctx("2", ViewerLevel));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("Second Song");
        await music
            .Received(1)
            .RemoveFromQueueAsync(Broadcaster.ToString(), 3, Arg.Any<CancellationToken>());
        await music.DidNotReceiveWithAnyArgs().SkipAsync(default!, default!);
        await music.DidNotReceiveWithAnyArgs().PlayAsync(default!);
    }

    [Fact]
    public async Task Skip_N_cannot_remove_another_users_request()
    {
        // The caller has exactly ONE own request; asking for their "2nd" must be refused rather than
        // falling through to someone else's entry at that global position.
        MusicQueueItem callersOnly = Item("Only Mine", "Bamo");
        MusicQueueItem someoneElses = Item("Not Mine", "SomeoneElse");
        MusicQueue queue = new(CurrentTrack: null, Queue: [callersOnly, someoneElses]);

        IMusicService music = Substitute.For<IMusicService>();
        music.GetQueueAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>()).Returns(queue);
        SkipBuiltin sut = new(
            music,
            TestBuiltinComposer.Create(),
            MusicGateTestKit.Gate(holdsGrant: false)
        );

        Result<string> result = await sut.ExecuteAsync(Ctx("2", ViewerLevel));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain("Not Mine");
        await music.DidNotReceiveWithAnyArgs().RemoveFromQueueAsync(default!, default);
        await music.DidNotReceiveWithAnyArgs().SkipAsync(default!, default!);
    }

    [Fact]
    public async Task Bare_skip_by_a_non_moderator_holding_the_music_queue_moderate_grant_skips_the_track()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .SkipAsync(
                Broadcaster.ToString(),
                "twitch-42",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        SkipBuiltin sut = new(
            music,
            TestBuiltinComposer.Create(),
            MusicGateTestKit.Gate(holdsGrant: true, out IRoleResolver roles)
        );

        Result<string> result = await sut.ExecuteAsync(Ctx(string.Empty, ViewerLevel));

        result.Value.Should().Be("Skipped to the next track.");
        await music
            .Received(1)
            .SkipAsync(
                Broadcaster.ToString(),
                "twitch-42",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        // The gate asked the Gate-2 resolver about the exact action the dashboard's POST /music/skip needs.
        await roles
            .Received(1)
            .HasCapabilityAsync(
                MusicGateTestKit.ChatterUserId,
                Broadcaster,
                "music:queue:moderate",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Bare_skip_by_a_non_moderator_without_the_grant_cannot_skip_someone_elses_song()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                new NowPlaying(
                    "Song",
                    "Artist",
                    null,
                    null,
                    1000,
                    0,
                    true,
                    50,
                    "SomeoneElse",
                    "spotify"
                )
            );
        SkipBuiltin sut = new(
            music,
            TestBuiltinComposer.Create(),
            MusicGateTestKit.Gate(holdsGrant: false)
        );

        Result<string> result = await sut.ExecuteAsync(Ctx(string.Empty, ViewerLevel));

        result.Value.Should().Be("You can only skip songs you requested yourself.");
        await music.DidNotReceiveWithAnyArgs().SkipAsync(default!, default!);
    }

    [Fact]
    public async Task The_volume_permission_refusal_is_the_system_permissiondenied_reply_a_channel_can_reword()
    {
        IMusicService music = Substitute.For<IMusicService>();
        FakeChannelBuiltinReplies channel = new FakeChannelBuiltinReplies().Set(
            Broadcaster,
            BuiltinResponseSlots.SystemReplies.Key,
            BuiltinResponseSlots.SystemReplies.PermissionDenied,
            "Mods only, friend."
        );
        VolumeBuiltin sut = new(
            music,
            TestBuiltinComposer.Create(channel),
            MusicGateTestKit.Gate(holdsGrant: false)
        );

        Result<string> result = await sut.ExecuteAsync(Ctx("40", ViewerLevel));

        result.Value.Should().Be("Mods only, friend.");
        await music.DidNotReceiveWithAnyArgs().SetVolumeAsync(default!, default);
    }

    [Fact]
    public async Task A_skip_with_no_player_open_tells_chat_to_open_the_player_not_that_it_skipped()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .SkipAsync(
                Broadcaster.ToString(),
                "twitch-42",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure(
                    "Nothing is playing on any device right now.",
                    "NO_ACTIVE_DEVICE",
                    errorData: new NoActivePlayerRefusal("youtube")
                )
            );
        SkipBuiltin sut = new(
            music,
            TestBuiltinComposer.Create(),
            MusicGateTestKit.Gate(holdsGrant: false)
        );

        Result<string> result = await sut.ExecuteAsync(Ctx(string.Empty, ModeratorLevel));

        result.Value.Should().Be("The music player isn't open — add the Audio widget to OBS.");
        result.Value.Should().NotContainEquivalentOf("spotify");
    }

    [Fact]
    public async Task A_skip_with_no_Spotify_device_playing_tells_chat_to_open_Spotify()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .SkipAsync(
                Broadcaster.ToString(),
                "twitch-42",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure(
                    "Nothing is playing on any device right now.",
                    "NO_ACTIVE_DEVICE",
                    errorData: new NoActivePlayerRefusal("spotify")
                )
            );
        SkipBuiltin sut = new(
            music,
            TestBuiltinComposer.Create(),
            MusicGateTestKit.Gate(holdsGrant: false)
        );

        Result<string> result = await sut.ExecuteAsync(Ctx(string.Empty, ModeratorLevel));

        result.Value.Should().Be("No Spotify device is playing — open Spotify on a device.");
    }

    [Fact]
    public async Task A_blocked_YouTube_skip_with_the_audio_widget_closed_answers_from_the_no_player_slot()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .SkipAsync(
                Broadcaster.ToString(),
                "twitch-42",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure(
                    "Nothing is playing on any device right now.",
                    "NO_ACTIVE_DEVICE",
                    errorData: new NoActivePlayerRefusal("youtube")
                )
            );
        SkipBuiltin sut = new(
            music,
            TestBuiltinComposer.Create(),
            MusicGateTestKit.Gate(holdsGrant: false)
        );

        Result<string> result = await sut.ExecuteAsync(Ctx(string.Empty, ModeratorLevel));

        result
            .Value.Should()
            .Be(
                ToneTemplateCatalog.Get(
                    PersonalityTone.Informative,
                    BuiltinResponseSlots.Skip.Key,
                    BuiltinResponseSlots.Skip.NoPlayer
                )[0]
            );
        result.Value.Should().NotContainEquivalentOf("queue");
    }

    [Theory]
    [InlineData("MUSIC_AUTH_FAILED", "notconnected")]
    [InlineData("MUSIC_FORBIDDEN", "forbidden")]
    public async Task A_skip_blocked_by_the_music_connection_answers_from_its_own_skip_slot(
        string errorCode,
        string slot
    )
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .SkipAsync(
                Broadcaster.ToString(),
                "twitch-42",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("internal detail 5521", errorCode));
        SkipBuiltin sut = new(
            music,
            TestBuiltinComposer.Create(),
            MusicGateTestKit.Gate(holdsGrant: false)
        );

        Result<string> result = await sut.ExecuteAsync(Ctx(string.Empty, ModeratorLevel));

        result
            .Value.Should()
            .Be(ToneTemplateCatalog.Get(PersonalityTone.Informative, "skip", slot)[0]);
        result.Value.Should().NotContain("5521").And.NotContainEquivalentOf("queue");
    }

    [Fact]
    public async Task A_skip_the_provider_refuses_answers_from_its_slot_never_the_services_own_sentence()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .SkipAsync(
                Broadcaster.ToString(),
                "twitch-42",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("internal provider detail 7731", "PREMIUM_REQUIRED"));
        SkipBuiltin sut = new(
            music,
            TestBuiltinComposer.Create(),
            MusicGateTestKit.Gate(holdsGrant: false)
        );

        Result<string> result = await sut.ExecuteAsync(Ctx(string.Empty, ModeratorLevel));

        result
            .Value.Should()
            .Be(
                ToneTemplateCatalog.Get(
                    PersonalityTone.Informative,
                    BuiltinResponseSlots.Skip.Key,
                    BuiltinResponseSlots.Skip.PremiumRequired
                )[0]
            );
        result.Value.Should().NotContain("7731");
    }

    [Fact]
    public async Task A_removed_request_reply_names_the_track_and_the_caller_through_its_slot()
    {
        MusicQueue queue = new(CurrentTrack: null, Queue: [Item("First Song", "Bamo")]);
        IMusicService music = Substitute.For<IMusicService>();
        music.GetQueueAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>()).Returns(queue);
        music
            .RemoveFromQueueAsync(Broadcaster.ToString(), 0, Arg.Any<CancellationToken>())
            .Returns(true);
        SkipBuiltin sut = new(
            music,
            TestBuiltinComposer.Create(),
            MusicGateTestKit.Gate(holdsGrant: false)
        );

        Result<string> result = await sut.ExecuteAsync(Ctx("1", ViewerLevel));

        result.Value.Should().Be("@Bamo Removed your request: First Song by Artist");
    }

    [Fact]
    public async Task Bare_skip_by_a_moderator_never_needs_the_grant_lookup()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .SkipAsync(
                Broadcaster.ToString(),
                "twitch-42",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        SkipBuiltin sut = new(
            music,
            TestBuiltinComposer.Create(),
            MusicGateTestKit.Gate(holdsGrant: false, out IRoleResolver roles)
        );

        await sut.ExecuteAsync(Ctx(string.Empty, ModeratorLevel));

        await music
            .Received(1)
            .SkipAsync(
                Broadcaster.ToString(),
                "twitch-42",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        await roles.DidNotReceiveWithAnyArgs().HasCapabilityAsync(default, default, default!);
    }

    [Fact]
    public async Task Volume_set_by_a_non_moderator_with_the_grant_reaches_the_provider_and_without_it_does_not()
    {
        IMusicService granted = Substitute.For<IMusicService>();
        granted
            .GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                new NowPlaying("Song", "Artist", null, null, 1000, 0, true, 50, null, "spotify")
            );
        granted
            .SetVolumeAsync(Broadcaster.ToString(), 40, Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        VolumeBuiltin allowed = new(
            granted,
            TestBuiltinComposer.Create(),
            MusicGateTestKit.Gate(holdsGrant: true)
        );
        Result<string> ok = await allowed.ExecuteAsync(Ctx("40", ViewerLevel));
        ok.Value.Should().Be("Volume set to 40%.");
        await granted
            .Received(1)
            .SetVolumeAsync(Broadcaster.ToString(), 40, Arg.Any<CancellationToken>());

        IMusicService denied = Substitute.For<IMusicService>();
        VolumeBuiltin refused = new(
            denied,
            TestBuiltinComposer.Create(),
            MusicGateTestKit.Gate(holdsGrant: false)
        );
        Result<string> no = await refused.ExecuteAsync(Ctx("40", ViewerLevel));
        no.Value.Should().Be("You don't have permission to use that command.");
        await denied.DidNotReceiveWithAnyArgs().SetVolumeAsync(default!, default);
    }

    [Fact]
    public async Task Bare_skip_still_skips_the_currently_playing_track_for_a_moderator()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .SkipAsync(
                Broadcaster.ToString(),
                "twitch-42",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        SkipBuiltin sut = new(
            music,
            TestBuiltinComposer.Create(),
            MusicGateTestKit.Gate(holdsGrant: false)
        );

        Result<string> result = await sut.ExecuteAsync(Ctx(string.Empty, ModeratorLevel));

        result.IsSuccess.Should().BeTrue();
        await music
            .Received(1)
            .SkipAsync(
                Broadcaster.ToString(),
                "twitch-42",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        await music.DidNotReceiveWithAnyArgs().RemoveFromQueueAsync(default!, default);
    }

    [Fact]
    public async Task Bare_skip_from_a_plain_viewer_is_refused_and_never_skips_playback()
    {
        IMusicService music = Substitute.For<IMusicService>();
        SkipBuiltin sut = new(
            music,
            TestBuiltinComposer.Create(),
            MusicGateTestKit.Gate(holdsGrant: false)
        );

        Result<string> result = await sut.ExecuteAsync(Ctx(string.Empty, ViewerLevel));

        result.IsSuccess.Should().BeTrue();
        await music.DidNotReceiveWithAnyArgs().SkipAsync(default!, default!);
    }
}
