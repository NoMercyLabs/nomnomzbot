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
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Music.Services;
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
            .Returns(call => Task.FromResult(call.ArgAt<string>(0)));
        return new BuiltinResponseComposer(
            resolver,
            NoPlatformBuiltinReplies.Instance,
            FakeChannelBuiltinReplies.None
        );
    }

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
        SkipBuiltin sut = new(music, FakeComposer(), MusicGateTestKit.Gate(holdsGrant: false));

        Result<string> result = await sut.ExecuteAsync(Ctx("2", ViewerLevel));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("Second Song");
        await music
            .Received(1)
            .RemoveFromQueueAsync(Broadcaster.ToString(), 3, Arg.Any<CancellationToken>());
        await music.DidNotReceiveWithAnyArgs().SkipAsync(default!, default);
        await music.DidNotReceiveWithAnyArgs().PlayAsync(default!, default);
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
        SkipBuiltin sut = new(music, FakeComposer(), MusicGateTestKit.Gate(holdsGrant: false));

        Result<string> result = await sut.ExecuteAsync(Ctx("2", ViewerLevel));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain("Not Mine");
        await music.DidNotReceiveWithAnyArgs().RemoveFromQueueAsync(default!, default, default);
        await music.DidNotReceiveWithAnyArgs().SkipAsync(default!, default);
    }

    [Fact]
    public async Task Bare_skip_by_a_non_moderator_holding_the_music_queue_moderate_grant_skips_the_track()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .SkipAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        SkipBuiltin sut = new(
            music,
            FakeComposer(),
            MusicGateTestKit.Gate(holdsGrant: true, out IRoleResolver roles)
        );

        Result<string> result = await sut.ExecuteAsync(Ctx(string.Empty, ViewerLevel));

        // The composer picks one of the informative skip variants (ToneTemplateCatalog), not a fixed line.
        result.Value.Should().BeOneOf("Skipped.", "Track skipped.", "Skipped the current track.");
        await music.Received(1).SkipAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>());
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
    public async Task Bare_skip_by_a_non_moderator_without_the_grant_is_refused_and_skips_nothing()
    {
        IMusicService music = Substitute.For<IMusicService>();
        SkipBuiltin sut = new(music, FakeComposer(), MusicGateTestKit.Gate(holdsGrant: false));

        Result<string> result = await sut.ExecuteAsync(Ctx(string.Empty, ViewerLevel));

        result.Value.Should().Be("You don't have permission to use that command.");
        await music.DidNotReceiveWithAnyArgs().SkipAsync(default!, default);
    }

    [Fact]
    public async Task Bare_skip_by_a_moderator_never_needs_the_grant_lookup()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .SkipAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        SkipBuiltin sut = new(
            music,
            FakeComposer(),
            MusicGateTestKit.Gate(holdsGrant: false, out IRoleResolver roles)
        );

        await sut.ExecuteAsync(Ctx(string.Empty, ModeratorLevel));

        await music.Received(1).SkipAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>());
        await roles
            .DidNotReceiveWithAnyArgs()
            .HasCapabilityAsync(default, default, default!, default);
    }

    [Fact]
    public async Task Volume_set_by_a_non_moderator_with_the_grant_reaches_the_provider_and_without_it_does_not()
    {
        IMusicService granted = Substitute.For<IMusicService>();
        granted
            .SetVolumeAsync(Broadcaster.ToString(), 40, Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        VolumeBuiltin allowed = new(
            granted,
            FakeComposer(),
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
            FakeComposer(),
            MusicGateTestKit.Gate(holdsGrant: false)
        );
        Result<string> no = await refused.ExecuteAsync(Ctx("40", ViewerLevel));
        no.Value.Should().Be("You don't have permission to use that command.");
        await denied.DidNotReceiveWithAnyArgs().SetVolumeAsync(default!, default, default);
    }

    [Fact]
    public async Task Bare_skip_still_skips_the_currently_playing_track_for_a_moderator()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .SkipAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        SkipBuiltin sut = new(music, FakeComposer(), MusicGateTestKit.Gate(holdsGrant: false));

        Result<string> result = await sut.ExecuteAsync(Ctx(string.Empty, ModeratorLevel));

        result.IsSuccess.Should().BeTrue();
        await music.Received(1).SkipAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>());
        await music.DidNotReceiveWithAnyArgs().RemoveFromQueueAsync(default!, default, default);
    }

    [Fact]
    public async Task Bare_skip_from_a_plain_viewer_is_refused_and_never_skips_playback()
    {
        IMusicService music = Substitute.For<IMusicService>();
        SkipBuiltin sut = new(music, FakeComposer(), MusicGateTestKit.Gate(holdsGrant: false));

        Result<string> result = await sut.ExecuteAsync(Ctx(string.Empty, ViewerLevel));

        result.IsSuccess.Should().BeTrue();
        await music.DidNotReceiveWithAnyArgs().SkipAsync(default!, default);
    }
}
