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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// <c>!followage</c> (legacy parity, Followage.cs): a follower hears how long they have followed, in the old
/// bot's wording; a non-follower hears "You are not following!"; a failed Helix lookup gets a real error line,
/// never a fabricated duration. Every other tone keeps its voice but carries the same facts.
/// </summary>
public sealed class FollowAgeBuiltinTests
{
    private const string TwitchId = "42";
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    private readonly ITwitchChannelsApi _channels = Substitute.For<ITwitchChannelsApi>();
    private readonly Guid _channel = Guid.CreateVersion7();

    private BuiltinCommandContext Context(
        string personality = PersonalityTone.Informative,
        string? platform = null
    ) =>
        new()
        {
            BroadcasterId = _channel,
            TriggeringUserId = TwitchId,
            TriggeringUserDisplayName = "Stoney_Eagle",
            TriggeringUserLogin = "stoney_eagle",
            Personality = personality,
            TriggeringPlatform = platform,
        };

    private FollowAgeBuiltin Builtin() =>
        new(_channels, TestBuiltinComposer.Create(), new FakeTimeProvider(Now));

    private void Follows(TimeSpan ago) =>
        _channels
            .GetChannelFollowerAsync(_channel, TwitchId, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<TwitchChannelFollower?>(
                    new(TwitchId, "stoney_eagle", "Stoney_Eagle", Now - ago)
                )
            );

    private void DoesNotFollow() =>
        _channels
            .GetChannelFollowerAsync(_channel, TwitchId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<TwitchChannelFollower?>(null));

    [Fact]
    public async Task A_follower_hears_the_legacy_sentence_with_the_real_duration()
    {
        Follows(TimeSpan.FromDays(385));

        Result<string> result = await Builtin().ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("You have been following for 1 year and 20 days!");
        await _channels
            .Received(1)
            .GetChannelFollowerAsync(_channel, TwitchId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_fresh_follower_hears_hours_and_minutes()
    {
        Follows(TimeSpan.FromHours(5));
        (await Builtin().ExecuteAsync(Context()))
            .Value.Should()
            .Be("You have been following for 5 hours!");

        Follows(TimeSpan.FromMinutes(10));
        (await Builtin().ExecuteAsync(Context()))
            .Value.Should()
            .Be("You have been following for 10 minutes!");
    }

    [Fact]
    public async Task A_viewer_who_does_not_follow_hears_the_legacy_not_following_reply()
    {
        DoesNotFollow();

        Result<string> result = await Builtin().ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("You are not following!");
        result.Value.Should().NotContain("following for");
    }

    [Fact]
    public async Task A_Helix_failure_gets_a_real_error_reply_never_a_duration()
    {
        _channels
            .GetChannelFollowerAsync(_channel, TwitchId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<TwitchChannelFollower?>("Helix timed out.", "TIMEOUT"));

        Result<string> result = await Builtin().ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue("the command always answers rather than going silent");
        result.Value.Should().Be("Twitch did not answer just now — try again in a moment.");
        result.Value.Should().NotContain("unknown").And.NotContain("following for");
    }

    [Fact]
    public async Task Sassy_tone_keeps_its_voice_but_carries_the_same_facts()
    {
        Follows(TimeSpan.FromDays(385));
        Result<string> age = await Builtin().ExecuteAsync(Context(PersonalityTone.Sassy));
        DoesNotFollow();
        Result<string> notFollowing = await Builtin().ExecuteAsync(Context(PersonalityTone.Sassy));

        age.Value.Should().NotBe("You have been following for 1 year and 20 days!");
        age.Value.Should().Contain("1 year and 20 days");
        notFollowing.Value.Should().NotBe("You are not following!");
        notFollowing.Value.Should().Contain("not following").And.NotContain("following for");
    }

    [Fact]
    public async Task A_non_Twitch_chatter_is_never_sent_to_Helix()
    {
        Result<string> result = await Builtin()
            .ExecuteAsync(Context(platform: AuthEnums.Platform.Kick));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        await _channels
            .DidNotReceive()
            .GetChannelFollowerAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }
}
