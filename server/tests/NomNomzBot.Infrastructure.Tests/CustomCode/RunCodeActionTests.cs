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
using NomNomzBot.Domain.Platform;
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
            Parameters = id is { } g
                ? new Dictionary<string, JsonElement>
                {
                    ["code_script_id"] = JsonSerializer.SerializeToElement(g.ToString()),
                }
                : null,
        };

    private static ActionDefinition ActionWithRawCodeScriptId(string codeScriptId) =>
        new()
        {
            Type = "run_code",
            Parameters = new Dictionary<string, JsonElement>
            {
                ["code_script_id"] = JsonSerializer.SerializeToElement(codeScriptId),
            },
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
                new(
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
                new(
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

    /// <summary>
    /// Reproduces the live bug on qtkitte's channel: the dashboard's code-script picker stores whatever id
    /// form the API last served it in — a 26-char ULID (UlidGuidJsonConverter) — and a step saved before the
    /// save-time normalization (PipelineService.SyncStepRowsFromGraphAsync /
    /// CommandConfigValidator.NormalizeResourceIdFields) landed carries that ULID in ConfigJson verbatim. A
    /// bare Guid.TryParse on a ULID always fails, so run_code never even reached the script runner
    /// (CodeScripts.LastRanAt stayed null) — the "!hug" command silently did nothing every time.
    /// </summary>
    [Fact]
    public async Task A_ulid_form_code_script_id_still_resolves_and_runs()
    {
        IScriptRunner runner = RunnerReturning(
            new(
                ScriptExecutionOutcome.Success,
                new Dictionary<string, string>(),
                "hugs!",
                StopPipeline: false,
                ErrorMessage: null,
                DenialReason: null
            )
        );
        RunCodeAction sut = new(runner);
        string ulid = OwnedIdCodec.Encode(ScriptId);

        ActionResult result = await sut.ExecuteAsync(Context(), ActionWithRawCodeScriptId(ulid));

        result.Succeeded.Should().BeTrue();
        result.Output.Should().Be("hugs!");
        await runner
            .Received(1)
            .RunAsync(ScriptId, Arg.Any<ScriptInvocation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_garbage_code_script_id_fails_with_the_same_message_as_missing()
    {
        RunCodeAction sut = new(Substitute.For<IScriptRunner>());

        ActionResult result = await sut.ExecuteAsync(
            Context(),
            ActionWithRawCodeScriptId("not-an-id")
        );

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("run_code requires a valid code_script_id.");
    }

    [Fact]
    public async Task A_non_success_outcome_fails_the_step()
    {
        RunCodeAction sut = new(
            RunnerReturning(
                new(
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
