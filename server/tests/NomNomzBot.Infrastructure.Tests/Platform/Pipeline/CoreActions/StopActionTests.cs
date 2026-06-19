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
using NomNomzBot.Infrastructure.Platform.Pipeline.CoreActions;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline.CoreActions;

public class InfraStopActionTests
{
    private static PipelineExecutionContext BuildCtx() =>
        new()
        {
            BroadcasterId = "chan",
            TriggeredByUserId = "user",
            TriggeredByDisplayName = "User",
            MessageId = "msg",
            RawMessage = "",
        };

    [Fact]
    public async Task ExecuteAsync_SetsShouldStopOnContext()
    {
        StopAction action = new();
        PipelineExecutionContext ctx = BuildCtx();
        ActionDefinition def = System.Text.Json.JsonSerializer.Deserialize<ActionDefinition>(
            """{"type":"stop"}"""
        )!;

        await action.ExecuteAsync(ctx, def);

        ctx.ShouldStop.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsSuccess()
    {
        StopAction action = new();
        PipelineExecutionContext ctx = BuildCtx();
        ActionDefinition def = System.Text.Json.JsonSerializer.Deserialize<ActionDefinition>(
            """{"type":"stop"}"""
        )!;

        ActionResult result = await action.ExecuteAsync(ctx, def);

        result.Succeeded.Should().BeTrue();
        result.Output.Should().Contain("stopped");
    }

    [Fact]
    public void ActionType_IsStop()
    {
        StopAction action = new();
        action.ActionType.Should().Be("stop");
    }
}
