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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Domain.CustomCode.Enums;
using NomNomzBot.Infrastructure.CustomCode.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// Proves the run_code pipeline action (custom-code.md §6): a successful run surfaces the script's output and merges
/// its variable writes back into the pipeline; the stop flag halts the pipeline; a missing code_script_id and a
/// non-success outcome both fail the step (fail-closed).
/// </summary>
public sealed class RunCodeActionTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000b001");
    private static readonly Guid ScriptId = Guid.Parse("0192a000-0000-7000-8000-00000000b0aa");

    private static ActionDefinition Action(Guid? id) =>
        new()
        {
            Type = "run_code",
            Parameters = id is Guid g
                ? new Dictionary<string, JsonElement>
                {
                    ["code_script_id"] = JsonSerializer.SerializeToElement(g.ToString()),
                }
                : null,
        };

    private static PipelineExecutionContext Context() =>
        new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "u1",
            TriggeredByDisplayName = "User",
            MessageId = "m1",
            RawMessage = "!cmd a b",
        };

    private static IScriptRunner RunnerReturning(ScriptRunResult result)
    {
        IScriptRunner runner = Substitute.For<IScriptRunner>();
        runner
            .RunAsync(Arg.Any<Guid>(), Arg.Any<ScriptInvocation>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(result));
        return runner;
    }

    [Fact]
    public async Task A_successful_run_surfaces_output_and_merges_variables()
    {
        RunCodeAction sut = new(
            RunnerReturning(
                new ScriptRunResult(
                    ScriptExecutionOutcome.Success,
                    new Dictionary<string, string> { ["x"] = "1" },
                    "hello",
                    StopPipeline: false,
                    ErrorMessage: null,
                    DenialReason: null
                )
            )
        );
        PipelineExecutionContext ctx = Context();

        ActionResult result = await sut.ExecuteAsync(ctx, Action(ScriptId));

        result.Succeeded.Should().BeTrue();
        result.Output.Should().Be("hello");
        ctx.Variables["x"].Should().Be("1");
    }

    [Fact]
    public async Task The_stop_flag_halts_the_pipeline()
    {
        RunCodeAction sut = new(
            RunnerReturning(
                new ScriptRunResult(
                    ScriptExecutionOutcome.Success,
                    new Dictionary<string, string>(),
                    null,
                    StopPipeline: true,
                    ErrorMessage: null,
                    DenialReason: null
                )
            )
        );
        PipelineExecutionContext ctx = Context();

        await sut.ExecuteAsync(ctx, Action(ScriptId));

        ctx.ShouldStop.Should().BeTrue();
    }

    [Fact]
    public async Task A_missing_code_script_id_fails()
    {
        RunCodeAction sut = new(Substitute.For<IScriptRunner>());

        ActionResult result = await sut.ExecuteAsync(Context(), Action(null));

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task A_non_success_outcome_fails_the_step()
    {
        RunCodeAction sut = new(
            RunnerReturning(
                new ScriptRunResult(
                    ScriptExecutionOutcome.Faulted,
                    new Dictionary<string, string>(),
                    null,
                    StopPipeline: false,
                    ErrorMessage: "boom",
                    DenialReason: null
                )
            )
        );

        ActionResult result = await sut.ExecuteAsync(Context(), Action(ScriptId));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("boom");
    }
}
