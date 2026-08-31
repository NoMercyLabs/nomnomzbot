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
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Platform;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// <c>!whisper &lt;user&gt; &lt;message&gt;</c> (legacy parity, S068c) proves the REAL side effect: the
/// target login is actually resolved to a Twitch id via <see cref="ITwitchUsersApi"/>, and the Twitch
/// <see cref="IPlatformDirectMessageSender"/> is actually invoked with that id and the exact message —
/// not merely "no exception".
/// </summary>
public sealed class WhisperBuiltinTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-000000009903");

    private static BuiltinCommandContext Context(string args) =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeringUserId = "mod-1",
            TriggeringUserDisplayName = "SomeMod",
            Args = args,
        };

    private static TwitchUser Viewer1() =>
        new(
            Id: "999",
            Login: "viewer1",
            DisplayName: "Viewer1",
            Type: "",
            BroadcasterType: "",
            Description: "",
            ProfileImageUrl: "",
            OfflineImageUrl: "",
            ViewCount: 0,
            CreatedAt: DateTimeOffset.UtcNow
        );

    [Fact]
    public async Task Resolves_the_target_login_and_sends_the_exact_message_to_the_real_provider_user_id()
    {
        ITwitchUsersApi twitchUsers = Substitute.For<ITwitchUsersApi>();
        twitchUsers
            .GetUsersByLoginsAsync(
                Arg.Is<IReadOnlyList<string>>(logins => logins.Single() == "viewer1"),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([Viewer1()]));

        IPlatformDirectMessageSender twitchSender = Substitute.For<IPlatformDirectMessageSender>();
        twitchSender.Provider.Returns(AuthEnums.Platform.Twitch);
        twitchSender
            .SendAsync(Broadcaster, "999", "hey stop that", Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        WhisperBuiltin sut = new(twitchUsers, [twitchSender]);

        Result<string> result = await sut.ExecuteAsync(Context("@viewer1 hey stop that"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("Viewer1");

        await twitchSender
            .Received(1)
            .SendAsync(Broadcaster, "999", "hey stop that", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_login_never_calls_the_dm_sender()
    {
        ITwitchUsersApi twitchUsers = Substitute.For<ITwitchUsersApi>();
        twitchUsers
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([]));

        IPlatformDirectMessageSender twitchSender = Substitute.For<IPlatformDirectMessageSender>();
        twitchSender.Provider.Returns(AuthEnums.Platform.Twitch);

        WhisperBuiltin sut = new(twitchUsers, [twitchSender]);

        Result<string> result = await sut.ExecuteAsync(Context("ghostuser hello"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("Could not find");

        await twitchSender
            .DidNotReceive()
            .SendAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Missing_message_argument_never_looks_up_a_user()
    {
        ITwitchUsersApi twitchUsers = Substitute.For<ITwitchUsersApi>();
        WhisperBuiltin sut = new(twitchUsers, []);

        Result<string> result = await sut.ExecuteAsync(Context("viewer1"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("Usage");

        await twitchUsers
            .DidNotReceive()
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }
}
