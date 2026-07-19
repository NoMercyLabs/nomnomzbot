// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NomNomzBot.Infrastructure.Platform.Pipeline.CoreActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

/// <summary>
/// Proves the engine exposes each step's outcome as the pipeline variables {last.success}/{last.error}/
/// {last.output}, so a LATER step can branch on whether the previous action failed. Combined with a step's
/// existing continue_on_error opt-in, this is the generic building block behind the legacy reward-refund
/// flows: <c>play_tts (continue_on_error) → redemption_refund (condition: {last.success} == false)</c>.
/// A stub resolver does plain {key} substitution — enough for a comparison on a seeded variable.
/// </summary>
public sealed class PipelineOutcomeVariableTests
{
    private static readonly Guid TestChannel = Guid.Parse("0192a000-0000-7000-8000-0000000000c9");

    // Plain {key} substitution from the supplied variables — the only resolver behavior a comparison
    // on {last.success} needs. Mirrors ITemplateResolver.Resolve semantics without the DB-backed engine.
    private sealed class StubResolver : ITemplateResolver
    {
        public string Resolve(string template, IDictionary<string, string> variables) =>
            Regex.Replace(
                template,
                "\\{([^{}]+)\\}",
                m => variables.TryGetValue(m.Groups[1].Value.Trim(), out string? v) ? v : m.Value
            );

        public Task<string> ResolveAsync(
            string template,
            IDictionary<string, string> seedVariables,
            Guid? broadcasterId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Resolve(template, seedVariables));
    }

    private static PipelineEngine CreateEngine()
    {
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Arg.Any<Guid>()).Returns((ChannelContext?)null);

        ICommandAction[] actions = [new StopAction(), new SetVariableAction()];
        ICommandCondition[] conditions = [new ComparisonCondition(new StubResolver())];

        NomNomzBot.Application.Abstractions.Persistence.IApplicationDbContext db =
            Substitute.For<NomNomzBot.Application.Abstractions.Persistence.IApplicationDbContext>();

        return new PipelineEngine(
            db,
            registry,
            actions,
            conditions,
            NullLogger<PipelineEngine>.Instance,
            TimeProvider.System
        );
    }

    private static PipelineRequest Request(string json) =>
        new()
        {
            BroadcasterId = TestChannel,
            TriggeredByUserId = "u1",
            TriggeredByDisplayName = "TestUser",
            PipelineJson = json,
            MessageId = "m1",
            RawMessage = "",
        };

    [Fact]
    public async Task FailedStepWithContinueOnError_LetsALaterStepBranchOnLastSuccessFalse()
    {
        // Step 0: an unknown action fails, but continue_on_error keeps the pipeline going → {last.success}=false.
        // Step 1: `stop` gated on {last.success} == false → the condition is TRUE, so the refund-style step runs.
        const string json = """
            {"steps":[
              {"action":{"type":"does_not_exist"},"continue_on_error":true},
              {"action":{"type":"stop"},"condition":{"type":"comparison","operator":"==","left":"{last.success}","right":"false"}}
            ]}
            """;

        PipelineExecutionResult result = await CreateEngine().ExecuteAsync(Request(json));

        result.StepLogs.Should().HaveCount(2);
        result.StepLogs[0].Succeeded.Should().BeFalse("the unknown action fails");
        result
            .StepLogs[1]
            .Output.Should()
            .NotBe(
                "Condition not met — step skipped",
                "the failure exposed {last.success}=false so the conditional recovery step runs"
            );
        result.StepLogs[1].ActionType.Should().Be("stop");
    }

    [Fact]
    public async Task SucceedingStep_SetsLastSuccessTrue_SoAFailureGatedStepIsSkipped()
    {
        // Step 0 succeeds → {last.success}=true. Step 1's "only on failure" guard must NOT fire.
        const string json = """
            {"steps":[
              {"action":{"type":"set_variable","name":"x","value":"1"}},
              {"action":{"type":"stop"},"condition":{"type":"comparison","operator":"==","left":"{last.success}","right":"false"}}
            ]}
            """;

        PipelineExecutionResult result = await CreateEngine().ExecuteAsync(Request(json));

        result.StepLogs.Should().HaveCount(2);
        result.StepLogs[0].Succeeded.Should().BeTrue();
        result
            .StepLogs[1]
            .Output.Should()
            .Be(
                "Condition not met — step skipped",
                "a succeeding prior step leaves {last.success}=true so the failure-gated step is skipped"
            );
    }

    [Fact]
    public async Task LastOutputVariable_CarriesTheSucceedingActionsOutput()
    {
        // set_variable reports "Set x = hello"; a comparison on {last.output} containing that proves the
        // output is exposed for a later step to read.
        const string json = """
            {"steps":[
              {"action":{"type":"set_variable","name":"x","value":"hello"}},
              {"action":{"type":"stop"},"condition":{"type":"comparison","operator":"contains","left":"{last.output}","right":"hello"}}
            ]}
            """;

        PipelineExecutionResult result = await CreateEngine().ExecuteAsync(Request(json));

        result
            .StepLogs[1]
            .Output.Should()
            .NotBe(
                "Condition not met — step skipped",
                "{last.output} exposed the prior step's output"
            );
    }
}
