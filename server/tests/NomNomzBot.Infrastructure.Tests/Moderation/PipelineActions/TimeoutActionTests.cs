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
/// S012: the pipeline "timeout" action (<see cref="TimeoutAction"/>) calls <see cref="IChatProvider"/> directly,
/// bypassing <c>ModerationService</c>'s guard entirely — a rule/command author who wires up a "duration" value
/// that resolves to zero or a negative number must NOT reach Twitch with it. A string the JSON parameter deserializer
/// can't read as a number already falls back to the 60s default via <see cref="ActionDefinition.GetInt"/> (no
/// change needed there); this closes the remaining gap — an explicit non-positive number.
/// </summary>
public sealed class TimeoutActionTests
{
    private static ActionDefinition ActionWithDuration(int duration) =>
        new()
        {
            Type = "timeout",
            Parameters = new()
            {
                ["user_id"] = JsonSerializer.SerializeToElement("target-123"),
                ["duration"] = JsonSerializer.SerializeToElement(duration),
            },
        };

    private static PipelineExecutionContext NewContext() =>
        new()
        {
            BroadcasterId = Guid.NewGuid(),
            TriggeredByUserId = "mod-1",
            TriggeredByDisplayName = "Mod",
            MessageId = "msg-1",
            RawMessage = "!timeout target-123",
        };

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-3600)]
    public async Task ExecuteAsync_WithZeroOrNegativeDuration_FailsAndNeverCallsTheChatProvider(
        int duration
    )
    {
        IChatProvider chat = Substitute.For<IChatProvider>();
        TimeoutAction action = new(chat);

        ActionResult result = await action.ExecuteAsync(NewContext(), ActionWithDuration(duration));

        result.Succeeded.Should().BeFalse();
        await chat.Received(0)
            .TimeoutUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ExecuteAsync_WithAPositiveDuration_CallsTheChatProviderWithThatExactDuration()
    {
        IChatProvider chat = Substitute.For<IChatProvider>();
        TimeoutAction action = new(chat);

        ActionResult result = await action.ExecuteAsync(NewContext(), ActionWithDuration(600));

        result.Succeeded.Should().BeTrue();
        await chat.Received(1)
            .TimeoutUserAsync(
                Arg.Any<Guid>(),
                "target-123",
                600,
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }
}
