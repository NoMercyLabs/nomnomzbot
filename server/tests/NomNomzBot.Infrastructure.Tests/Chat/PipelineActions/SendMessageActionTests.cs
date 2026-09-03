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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Infrastructure.Chat.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat.PipelineActions;

/// <summary>
/// The <c>send_message</c> action's optional <c>sender</c> field: defaults to the bot voice (every step
/// authored before this field existed keeps behaving exactly as before), and <c>"broadcaster"</c> routes
/// the send through <see cref="IChatProvider.SendMessageAsBroadcasterAsync"/> instead — the streamer's own
/// account, for content only they can post as themselves (a subscriber-only emote a separate bot account
/// is not subscribed to, say).
/// </summary>
public sealed class SendMessageActionTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0198b000-0000-7000-8000-00000000f001");

    private static (SendMessageAction Action, IChatProvider Chat) Build()
    {
        IChatProvider chat = Substitute.For<IChatProvider>();
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci => Task.FromResult((string)ci[0]));

        return (new(chat, resolver), chat);
    }

    private static PipelineExecutionContext BuildContext() =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeredByUserId = "u1",
            TriggeredByDisplayName = "Viewer",
            MessageId = "msg-1",
            RawMessage = "!cmd",
        };

    [Fact]
    public async Task No_sender_field_sends_via_the_bot_voice_unchanged()
    {
        (SendMessageAction action, IChatProvider chat) = Build();
        chat.SendMessageAsync(Broadcaster, "hello", Arg.Any<CancellationToken>()).Returns(true);

        ActionResult result = await action.ExecuteAsync(
            BuildContext(),
            new()
            {
                Type = "send_message",
                Parameters = new() { ["message"] = ParamValue("hello") },
            }
        );

        result.Succeeded.Should().BeTrue();
        await chat.Received(1).SendMessageAsync(Broadcaster, "hello", Arg.Any<CancellationToken>());
        await chat.DidNotReceiveWithAnyArgs()
            .SendMessageAsBroadcasterAsync(default, default!, default);
    }

    [Fact]
    public async Task Sender_bot_sends_via_the_bot_voice()
    {
        (SendMessageAction action, IChatProvider chat) = Build();
        chat.SendMessageAsync(Broadcaster, "hello", Arg.Any<CancellationToken>()).Returns(true);

        ActionResult result = await action.ExecuteAsync(
            BuildContext(),
            new()
            {
                Type = "send_message",
                Parameters = new()
                {
                    ["message"] = ParamValue("hello"),
                    ["sender"] = ParamValue("bot"),
                },
            }
        );

        result.Succeeded.Should().BeTrue();
        await chat.Received(1).SendMessageAsync(Broadcaster, "hello", Arg.Any<CancellationToken>());
        await chat.DidNotReceiveWithAnyArgs()
            .SendMessageAsBroadcasterAsync(default, default!, default);
    }

    [Fact]
    public async Task Sender_broadcaster_sends_as_the_streamer_not_the_bot()
    {
        (SendMessageAction action, IChatProvider chat) = Build();
        chat.SendMessageAsBroadcasterAsync(Broadcaster, "hello", Arg.Any<CancellationToken>())
            .Returns(true);

        ActionResult result = await action.ExecuteAsync(
            BuildContext(),
            new()
            {
                Type = "send_message",
                Parameters = new()
                {
                    ["message"] = ParamValue("hello"),
                    ["sender"] = ParamValue("broadcaster"),
                },
            }
        );

        result.Succeeded.Should().BeTrue();
        await chat.Received(1)
            .SendMessageAsBroadcasterAsync(Broadcaster, "hello", Arg.Any<CancellationToken>());
        await chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default);
    }

    [Fact]
    public async Task A_rejected_broadcaster_send_fails_the_step()
    {
        (SendMessageAction action, IChatProvider chat) = Build();
        chat.SendMessageAsBroadcasterAsync(Broadcaster, "hello", Arg.Any<CancellationToken>())
            .Returns(false);

        ActionResult result = await action.ExecuteAsync(
            BuildContext(),
            new()
            {
                Type = "send_message",
                Parameters = new()
                {
                    ["message"] = ParamValue("hello"),
                    ["sender"] = ParamValue("broadcaster"),
                },
            }
        );

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    private static System.Text.Json.JsonElement ParamValue(string value) =>
        System.Text.Json.JsonSerializer.SerializeToElement(value);
}
