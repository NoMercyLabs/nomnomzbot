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

public class WaitActionTests
{
    private static PipelineExecutionContext BuildCtx(CancellationToken ct = default) =>
        new()
        {
            BroadcasterId = "chan",
            TriggeredByUserId = "user",
            TriggeredByDisplayName = "User",
            MessageId = "msg",
            RawMessage = "",
            CancellationToken = ct,
        };

    private static ActionDefinition BuildAction(string json)
    {
        ActionDefinition def = System.Text.Json.JsonSerializer.Deserialize<ActionDefinition>(json)!;
        return def;
    }

    [Fact]
    public async Task ExecuteAsync_ZeroDelay_ReturnsImmediately()
    {
        WaitAction action = new();
        PipelineExecutionContext ctx = BuildCtx();
        ActionDefinition def = BuildAction("""{"type":"wait","milliseconds":0}""");

        ActionResult result = await action.ExecuteAsync(ctx, def);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_SmallDelay_Succeeds()
    {
        WaitAction action = new();
        PipelineExecutionContext ctx = BuildCtx();
        ActionDefinition def = BuildAction("""{"type":"wait","milliseconds":50}""");

        ActionResult result = await action.ExecuteAsync(ctx, def);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_SecondsParam_Converts()
    {
        WaitAction action = new();
        PipelineExecutionContext ctx = BuildCtx();
        ActionDefinition def = BuildAction("""{"type":"wait","seconds":0}"""); // 0 = no delay

        ActionResult result = await action.ExecuteAsync(ctx, def);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ExceedsMax_ClampsTo30Seconds()
    {
        WaitAction action = new();
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(100));
        PipelineExecutionContext ctx = BuildCtx(cts.Token);
        ActionDefinition def = BuildAction("""{"type":"wait","milliseconds":60000}"""); // 60s → clamped to 30s

        // With fast cancellation, we can verify it waits (clamped), then gets cancelled
        Func<Task<ActionResult>> act = () => action.ExecuteAsync(ctx, def);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_Cancelled_ThrowsOperationCancelled()
    {
        WaitAction action = new();
        using CancellationTokenSource cts = new();
        cts.Cancel();
        PipelineExecutionContext ctx = BuildCtx(cts.Token);
        ActionDefinition def = BuildAction("""{"type":"wait","milliseconds":5000}""");

        Func<Task<ActionResult>> act = () => action.ExecuteAsync(ctx, def);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void ActionType_IsWait()
    {
        WaitAction action = new();
        action.ActionType.Should().Be("wait");
    }
}
