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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Stream.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stream.PipelineActions;

/// <summary>
/// Proves the shoutout action's target resolution: a numeric Twitch id passes straight through, a
/// login/channel name (the form a curated auto-shoutout list holds, @ tolerated) resolves to its id via
/// Helix Get Users before the shoutout is sent, and an unknown login fails without hitting the shoutout API.
/// </summary>
public sealed class ShoutoutActionTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000b201");

    private static PipelineExecutionContext Ctx() =>
        new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "tw-1",
            TriggeredByDisplayName = "Viewer",
            MessageId = "m1",
            RawMessage = "!so target",
        };

    private static ActionDefinition Shoutout(string userId) =>
        new()
        {
            Type = "shoutout",
            Parameters = new Dictionary<string, JsonElement>
            {
                ["user_id"] = JsonSerializer.SerializeToElement(userId),
            },
        };

    private static TwitchUser User(string id, string login) =>
        new(
            Id: id,
            Login: login,
            DisplayName: login,
            Type: "",
            BroadcasterType: "",
            Description: "",
            ProfileImageUrl: "",
            OfflineImageUrl: "",
            ViewCount: 0,
            CreatedAt: DateTimeOffset.UnixEpoch
        );

    private static (ShoutoutAction Sut, ITwitchChatApi Chat, ITwitchUsersApi Users) Build()
    {
        ITwitchChatApi chat = Substitute.For<ITwitchChatApi>();
        chat.SendShoutoutAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        ITwitchUsersApi users = Substitute.For<ITwitchUsersApi>();

        ShoutoutAction sut = new(
            chat,
            users,
            Substitute.For<IChannelRegistry>(),
            TimeProvider.System,
            NullLogger<ShoutoutAction>.Instance
        );
        return (sut, chat, users);
    }

    [Fact]
    public async Task A_numeric_id_is_shouted_out_without_a_users_lookup()
    {
        (ShoutoutAction sut, ITwitchChatApi chat, ITwitchUsersApi users) = Build();

        ActionResult result = await sut.ExecuteAsync(Ctx(), Shoutout("123456"));

        result.Succeeded.Should().BeTrue();
        await chat.Received(1).SendShoutoutAsync(Channel, "123456", Arg.Any<CancellationToken>());
        await users.DidNotReceiveWithAnyArgs().GetUsersByLoginsAsync(default!, default);
    }

    [Fact]
    public async Task A_login_with_leading_at_resolves_to_its_id_before_the_shoutout()
    {
        (ShoutoutAction sut, ITwitchChatApi chat, ITwitchUsersApi users) = Build();
        users
            .GetUsersByLoginsAsync(
                Arg.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == "coolstreamer"),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([User("789", "coolstreamer")]));

        ActionResult result = await sut.ExecuteAsync(Ctx(), Shoutout("@CoolStreamer"));

        result.Succeeded.Should().BeTrue();
        await chat.Received(1).SendShoutoutAsync(Channel, "789", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_login_fails_without_calling_the_shoutout_api()
    {
        (ShoutoutAction sut, ITwitchChatApi chat, ITwitchUsersApi users) = Build();
        users
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([]));

        ActionResult result = await sut.ExecuteAsync(Ctx(), Shoutout("ghost_channel"));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ghost_channel");
        await chat.DidNotReceiveWithAnyArgs().SendShoutoutAsync(default, default!, default);
    }

    [Fact]
    public async Task A_variable_reference_resolves_from_the_pipeline_context_then_the_login_resolves()
    {
        // The rotating auto-shoutout shape: shoutout(user_id="{timer.message}") over a curated list.
        (ShoutoutAction sut, ITwitchChatApi chat, ITwitchUsersApi users) = Build();
        users
            .GetUsersByLoginsAsync(
                Arg.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == "rotationtarget"),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([User("456", "rotationtarget")]));

        PipelineExecutionContext ctx = Ctx();
        ctx.Variables["timer.message"] = "RotationTarget";

        ActionResult result = await sut.ExecuteAsync(ctx, Shoutout("{timer.message}"));

        result.Succeeded.Should().BeTrue();
        await chat.Received(1).SendShoutoutAsync(Channel, "456", Arg.Any<CancellationToken>());
    }
}
