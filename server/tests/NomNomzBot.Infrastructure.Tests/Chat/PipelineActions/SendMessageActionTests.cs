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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Infrastructure.Chat.PipelineActions;
using NomNomzBot.Infrastructure.Rewards.EventHandlers;
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

    private static (SendMessageAction Action, IChatProvider Chat) Build() =>
        BuildWithOverlay(new RecordingChatOverlayLineSender());

    private static (SendMessageAction Action, IChatProvider Chat) BuildWithOverlay(
        RecordingChatOverlayLineSender overlay
    )
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

        return (new(chat, resolver, TestReplyDispatcher.OverProvider(chat, overlay)), chat);
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
        await chat.DidNotReceiveWithAnyArgs().SendMessageAsBroadcasterAsync(default, default!);
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
        await chat.DidNotReceiveWithAnyArgs().SendMessageAsBroadcasterAsync(default, default!);
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
        await chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!);
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

    [Fact]
    public async Task A_600_char_line_is_sent_cut_to_447_chars_plus_an_ellipsis_like_the_old_bot()
    {
        (SendMessageAction action, IChatProvider chat) = Build();
        string line = new('a', 600);
        string expected = new string('a', 447) + "...";
        chat.SendMessageAsync(Broadcaster, expected, Arg.Any<CancellationToken>()).Returns(true);

        ActionResult result = await action.ExecuteAsync(
            BuildContext(),
            new()
            {
                Type = "send_message",
                Parameters = new() { ["message"] = ParamValue(line) },
            }
        );

        result.Succeeded.Should().BeTrue();
        await chat.Received(1)
            .SendMessageAsync(Broadcaster, expected, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_450_char_line_is_sent_unchanged()
    {
        (SendMessageAction action, IChatProvider chat) = Build();
        string line = new('b', 450);
        chat.SendMessageAsync(Broadcaster, line, Arg.Any<CancellationToken>()).Returns(true);

        ActionResult result = await action.ExecuteAsync(
            BuildContext(),
            new()
            {
                Type = "send_message",
                Parameters = new() { ["message"] = ParamValue(line) },
            }
        );

        result.Succeeded.Should().BeTrue();
        await chat.Received(1).SendMessageAsync(Broadcaster, line, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_reward_response_aimed_at_the_overlay_sends_the_overlay_line_and_no_chat_message()
    {
        RecordingChatOverlayLineSender overlay = new();
        (SendMessageAction action, IChatProvider chat) = BuildWithOverlay(overlay);

        ActionResult result = await action.ExecuteAsync(
            BuildContext(),
            RewardResponseStep("Thanks for the redeem", new(ReplyTarget.Overlay))
        );

        result.Succeeded.Should().BeTrue();
        overlay.Lines.Should().ContainSingle();
        // The overlay line is the bot's own line; the redeeming viewer is only named as the requester.
        overlay.Lines[0].Text.Should().Be("Thanks for the redeem");
        overlay.Lines[0].RequestedBy.Should().Be("Viewer");
        await chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!);
    }

    [Fact]
    public async Task A_reward_response_aimed_at_chat_sends_chat_and_nothing_to_the_overlay()
    {
        RecordingChatOverlayLineSender overlay = new();
        (SendMessageAction action, IChatProvider chat) = BuildWithOverlay(overlay);
        chat.SendMessageAsync(Broadcaster, "Thanks for the redeem", Arg.Any<CancellationToken>())
            .Returns(true);

        ActionResult result = await action.ExecuteAsync(
            BuildContext(),
            RewardResponseStep("Thanks for the redeem", new(ReplyTarget.Chat))
        );

        result.Succeeded.Should().BeTrue();
        await chat.Received(1)
            .SendMessageAsync(Broadcaster, "Thanks for the redeem", Arg.Any<CancellationToken>());
        overlay.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task A_reward_response_aimed_at_both_sends_chat_and_the_overlay_line()
    {
        RecordingChatOverlayLineSender overlay = new();
        (SendMessageAction action, IChatProvider chat) = BuildWithOverlay(overlay);
        chat.SendMessageAsync(Broadcaster, "Thanks", Arg.Any<CancellationToken>()).Returns(true);

        ActionResult result = await action.ExecuteAsync(
            BuildContext(),
            RewardResponseStep("Thanks", new(ReplyTarget.Both))
        );

        result.Succeeded.Should().BeTrue();
        await chat.Received(1)
            .SendMessageAsync(Broadcaster, "Thanks", Arg.Any<CancellationToken>());
        overlay.Lines.Should().ContainSingle();
    }

    [Fact]
    public async Task A_reward_response_aimed_nowhere_sends_nothing_and_still_succeeds()
    {
        RecordingChatOverlayLineSender overlay = new();
        (SendMessageAction action, IChatProvider chat) = BuildWithOverlay(overlay);

        ActionResult result = await action.ExecuteAsync(
            BuildContext(),
            RewardResponseStep("Thanks", new(ReplyTarget.None))
        );

        result.Succeeded.Should().BeTrue();
        await chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!);
        overlay.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task A_reward_response_with_no_delivery_builds_the_same_step_as_before_and_answers_in_chat()
    {
        RecordingChatOverlayLineSender overlay = new();
        (SendMessageAction action, IChatProvider chat) = BuildWithOverlay(overlay);
        chat.SendMessageAsync(Broadcaster, "Thanks", Arg.Any<CancellationToken>()).Returns(true);

        ActionResult result = await action.ExecuteAsync(
            BuildContext(),
            RewardResponseStep("Thanks", delivery: null)
        );

        result.Succeeded.Should().BeTrue();
        RewardRedeemedHandler
            .BuildResponsePipeline("Thanks", delivery: null)
            .Should()
            .Be(
                "{\"steps\":[{\"action\":{\"type\":\"send_message\",\"message\":\"Thanks\",\"target\":\"channel\"}}]}"
            );
        await chat.Received(1)
            .SendMessageAsync(Broadcaster, "Thanks", Arg.Any<CancellationToken>());
        overlay.Lines.Should().BeEmpty();
    }

    // The exact step a reward's simple Response runs as: the handler builds the JSON, the pipeline reads it.
    private static ActionDefinition RewardResponseStep(string message, ReplyDelivery? delivery)
    {
        string json = RewardRedeemedHandler.BuildResponsePipeline(message, delivery);
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json);
        System.Text.Json.JsonElement step = document
            .RootElement.GetProperty("steps")[0]
            .GetProperty("action");
        return System.Text.Json.JsonSerializer.Deserialize<ActionDefinition>(step.GetRawText())!;
    }

    private static System.Text.Json.JsonElement ParamValue(string value) =>
        System.Text.Json.JsonSerializer.SerializeToElement(value);
}
