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
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Infrastructure.Moderation.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation.PipelineActions;

/// <summary>
/// Proves the delete_message action reports what the platform did: a delete the platform accepted is a
/// success, a delete it refused (no usable token, not connected, rejected) is a failure the pipeline can
/// see, and the id falls back to the triggering message when the step names none.
/// </summary>
public sealed class DeleteMessageActionTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000d301");

    private static PipelineExecutionContext Ctx(string messageId = "m1") =>
        new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "tw-1",
            TriggeredByDisplayName = "Viewer",
            MessageId = messageId,
            RawMessage = "spam",
        };

    private static ActionDefinition Delete(string? messageId = null)
    {
        Dictionary<string, JsonElement> parameters = new();
        if (messageId is not null)
            parameters["message_id"] = JsonSerializer.SerializeToElement(messageId);
        return new() { Type = "delete_message", Parameters = parameters };
    }

    [Fact]
    public async Task ExecuteAsync_ARefusedDelete_IsAFailureThatNamesTheMessage()
    {
        IChatProvider chat = Substitute.For<IChatProvider>();
        chat.DeleteMessageAsync(Channel, "m1", Arg.Any<CancellationToken>()).Returns(false);
        DeleteMessageAction action = new(chat);

        ActionResult result = await action.ExecuteAsync(Ctx(), Delete());

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("m1");
    }

    [Fact]
    public async Task ExecuteAsync_AnAcceptedDelete_SucceedsAndTargetsTheNamedMessage()
    {
        IChatProvider chat = Substitute.For<IChatProvider>();
        chat.DeleteMessageAsync(Channel, "other", Arg.Any<CancellationToken>()).Returns(true);
        DeleteMessageAction action = new(chat);

        ActionResult result = await action.ExecuteAsync(Ctx(), Delete("other"));

        result.Succeeded.Should().BeTrue();
        await chat.Received(1).DeleteMessageAsync(Channel, "other", Arg.Any<CancellationToken>());
        await chat.DidNotReceive().DeleteMessageAsync(Channel, "m1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_NoMessageIdAnywhere_FailsWithoutCallingThePlatform()
    {
        IChatProvider chat = Substitute.For<IChatProvider>();
        DeleteMessageAction action = new(chat);

        ActionResult result = await action.ExecuteAsync(Ctx(string.Empty), Delete());

        result.Succeeded.Should().BeFalse();
        await chat.DidNotReceive()
            .DeleteMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
