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
/// The <c>send_reply</c> pipeline action must thread the REAL Twitch send outcome back into the pipeline
/// (S008) — a rejected reply falls back to a plain line that still addresses the triggering user, and only
/// a total failure (both the reply AND the fallback rejected) fails the step.
/// </summary>
public sealed class SendReplyActionTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0198b000-0000-7000-8000-00000000e001");

    private static (SendReplyAction Action, IChatProvider Chat) Build()
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
    public async Task ExecuteAsync_ReplyAccepted_ReturnsSuccessAndNeverFallsBack()
    {
        (SendReplyAction action, IChatProvider chat) = Build();
        chat.SendReplyAsync(Broadcaster, "msg-1", "hello", Arg.Any<CancellationToken>())
            .Returns(true);

        ActionResult result = await action.ExecuteAsync(
            BuildContext(),
            new()
            {
                Type = "send_reply",
                Parameters = new() { ["message"] = ParamValue("hello") },
            }
        );

        result.Succeeded.Should().BeTrue();
        await chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_ReplyRejected_FallsBackToPlainMentionAndReportsSuccess()
    {
        (SendReplyAction action, IChatProvider chat) = Build();
        chat.SendReplyAsync(Broadcaster, "msg-1", "hello", Arg.Any<CancellationToken>())
            .Returns(false); // Twitch rejects the reply form (e.g. a deleted parent message)
        chat.SendMessageAsync(Broadcaster, "@Viewer hello", Arg.Any<CancellationToken>())
            .Returns(true);

        ActionResult result = await action.ExecuteAsync(
            BuildContext(),
            new()
            {
                Type = "send_reply",
                Parameters = new() { ["message"] = ParamValue("hello") },
            }
        );

        result
            .Succeeded.Should()
            .BeTrue("the fallback delivered the line despite the rejected reply");
        await chat.Received(1)
            .SendMessageAsync(Broadcaster, "@Viewer hello", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ReplyAndFallbackBothRejected_ReturnsFailure()
    {
        (SendReplyAction action, IChatProvider chat) = Build();
        chat.SendReplyAsync(Broadcaster, "msg-1", "hello", Arg.Any<CancellationToken>())
            .Returns(false);
        chat.SendMessageAsync(Broadcaster, "@Viewer hello", Arg.Any<CancellationToken>())
            .Returns(false);

        ActionResult result = await action.ExecuteAsync(
            BuildContext(),
            new()
            {
                Type = "send_reply",
                Parameters = new() { ["message"] = ParamValue("hello") },
            }
        );

        result.Succeeded.Should().BeFalse("neither the reply nor the fallback ever reached chat");
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    private static System.Text.Json.JsonElement ParamValue(string value) =>
        System.Text.Json.JsonSerializer.SerializeToElement(value);
}
