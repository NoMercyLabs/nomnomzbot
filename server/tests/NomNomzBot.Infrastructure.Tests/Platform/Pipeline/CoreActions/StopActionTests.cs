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
using NomNomzBot.Infrastructure.Platform.Pipeline.CoreActions;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline.CoreActions;

public class StopActionTests
{
    private static PipelineExecutionContext BuildCtx() =>
        new()
        {
            BroadcasterId = Guid.Parse("0192a000-0000-7000-8000-0000000000c1"),
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
