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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NomNomzBot.Infrastructure.Tests.Chat;
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

    private const string TargetId = "777";

    private readonly ITwitchUsersApi _twitchUsers = Substitute.For<ITwitchUsersApi>();

    private BuiltinCommandContext Context(
        string personality = PersonalityTone.Informative,
        string? platform = null,
        string args = ""
    ) =>
        new()
        {
            BroadcasterId = _channel,
            TriggeringUserId = TwitchId,
            TriggeringUserDisplayName = "Stoney_Eagle",
            TriggeringUserLogin = "stoney_eagle",
            Personality = personality,
            TriggeringPlatform = platform,
            Args = args,
        };

    private FollowAgeBuiltin Builtin() =>
        new(
            _channels,
            _twitchUsers,
            TestMentionTargets.LoginsOnly(),
            TestBuiltinComposer.Create(),
            new FakeTimeProvider(Now)
        );

    private void TwitchKnows(string login, string displayName)
    {
        _twitchUsers
            .GetUsersByLoginsAsync(
                Arg.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == login),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success<IReadOnlyList<TwitchUser>>([
                    new TwitchUser(
                        Id: TargetId,
                        Login: login,
                        DisplayName: displayName,
                        Type: "",
                        BroadcasterType: "",
                        Description: "",
                        ProfileImageUrl: "",
                        OfflineImageUrl: "",
                        ViewCount: 0,
                        CreatedAt: Now.AddYears(-3)
                    ),
                ])
            );
    }

    private void TwitchKnowsNobody() =>
        _twitchUsers
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([]));

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
    public async Task A_target_is_looked_up_by_login_and_their_follow_record_is_read_not_the_callers()
    {
        TwitchKnows("ifillz", "iFiLLz");
        _channels
            .GetChannelFollowerAsync(_channel, TargetId, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<TwitchChannelFollower?>(
                    new(TargetId, "ifillz", "iFiLLz", Now - TimeSpan.FromDays(385))
                )
            );

        Result<string> result = await Builtin().ExecuteAsync(Context(args: "@iFiLLz"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("iFiLLz has been following for 1 year and 20 days!");
        await _twitchUsers
            .Received(1)
            .GetUsersByLoginsAsync(
                Arg.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == "ifillz"),
                Arg.Any<CancellationToken>()
            );
        await _channels
            .Received(1)
            .GetChannelFollowerAsync(_channel, TargetId, Arg.Any<CancellationToken>());
        await _channels
            .DidNotReceive()
            .GetChannelFollowerAsync(_channel, TwitchId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_target_who_does_not_follow_gets_the_not_following_other_reply()
    {
        TwitchKnows("ifillz", "iFiLLz");
        _channels
            .GetChannelFollowerAsync(_channel, TargetId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<TwitchChannelFollower?>(null));

        Result<string> result = await Builtin().ExecuteAsync(Context(args: "ifillz"));

        result.Value.Should().Be("iFiLLz is not following!");
    }

    [Fact]
    public async Task An_unknown_login_gets_the_not_found_reply_and_no_follow_lookup_is_made()
    {
        TwitchKnowsNobody();

        Result<string> result = await Builtin().ExecuteAsync(Context(args: "@nobody_here"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("@Stoney_Eagle Twitch has no user called nobody_here.");
        await _channels
            .DidNotReceive()
            .GetChannelFollowerAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_viewer_who_names_themselves_gets_the_caller_reply_without_a_user_lookup()
    {
        Follows(TimeSpan.FromDays(385));

        Result<string> result = await Builtin().ExecuteAsync(Context(args: "@Stoney_Eagle"));

        result.Value.Should().Be("You have been following for 1 year and 20 days!");
        await _twitchUsers
            .DidNotReceive()
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_user_lookup_gets_the_twitch_unavailable_reply()
    {
        _twitchUsers
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<TwitchUser>>("timeout", "TIMEOUT"));

        Result<string> result = await Builtin().ExecuteAsync(Context(args: "@ifillz"));

        result.Value.Should().Be("Twitch did not answer just now — try again in a moment.");
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
