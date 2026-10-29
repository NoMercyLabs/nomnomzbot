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
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Domain.CustomCode.Enums;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.CustomCode.Jint;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// Waiting on a host call is not script time (S-SCRIPT-TTS-WAIT). A tts.speak clip waits 7-15 s on the box, so a
/// script that only waits on its host calls must not run out of its own wall-clock budget, and that wait must
/// not be metered. A script that burns time on its own still times out, and every host call has its own
/// deadline, past which only that call fails, the way a refused call does.
/// </summary>
public sealed class ScriptHostWaitBudgetTests
{
    // A host edge that waits like a slow TTS provider: it honours the call's token, as the real bridge does.
    private sealed class SlowBridge(int waitMs, string? answer = "ok") : IScriptHostBridge
    {
        public HostImportDelegate Resolve(string capabilityKey) =>
            capabilityKey == ScriptHostErrorCodes.LastErrorKey
                ? (_, _, _) => null
                : (_, _, ct) =>
                {
                    Task.Delay(waitMs, ct).GetAwaiter().GetResult();
                    return answer;
                };
    }

    private static ScriptExecutionRequest Request(string js, ScriptResourceBudget budget) =>
        new("exec-1", js, "hash", new("u1", "User", [], new Dictionary<string, string>()), budget);

    private static ScriptCapabilityGrant Grant(params string[] keys) =>
        new(
            Guid.NewGuid(),
            [.. keys.Select(k => new ScriptCapabilityDescriptor(k, "low", "ff", true))]
        );

    [Fact]
    public async Task A_host_call_longer_than_the_run_budget_does_not_time_the_script_out()
    {
        JintScriptExecutor sut = new();
        ScriptResourceBudget budget = ScriptResourceBudget.Baseline with { WallClockMs = 1000 };

        ScriptExecutionOutcomeResult r = (
            await sut.ExecuteAsync(
                Request(
                    "bot.setVar('out', bot.call('tts.speak', 'hi')); bot.send('after');",
                    budget
                ),
                Grant("tts.speak"),
                new SlowBridge(3000)
            )
        ).Value;

        r.Outcome.Should().Be(ScriptExecutionOutcome.Success, r.ErrorMessage);
        r.VariablesOut["out"].Should().Be("ok");
        r.ChatOutput.Should().Be("after");
        r.HostWaitMs.Should().BeGreaterThanOrEqualTo(2900);
    }

    [Fact]
    public async Task A_script_that_loops_on_its_own_after_a_host_call_still_times_out()
    {
        JintScriptExecutor sut = new();
        ScriptResourceBudget budget = ScriptResourceBudget.Baseline with
        {
            WallClockMs = 1000,
            MaxFuelOrStatements = int.MaxValue,
        };

        ScriptExecutionOutcomeResult r = (
            await sut.ExecuteAsync(
                Request("bot.call('tts.speak', 'hi'); bot.send('spoke'); while (true) {}", budget),
                Grant("tts.speak"),
                new SlowBridge(1500)
            )
        ).Value;

        r.Outcome.Should().Be(ScriptExecutionOutcome.Timeout);
        r.ChatOutput.Should().Be("spoke");
        r.ElapsedMs.Should().BeLessThan(1500);
    }

    [Fact]
    public async Task A_host_call_past_its_own_deadline_fails_only_that_call()
    {
        JintScriptExecutor sut = new();
        ScriptResourceBudget budget = ScriptResourceBudget.Baseline with
        {
            WallClockMs = 30_000,
            MaxHostCallMs = 300,
        };

        ScriptExecutionOutcomeResult r = (
            await sut.ExecuteAsync(
                Request(
                    "var r = bot.call('tts.speak', 'hi');"
                        + " bot.setVar('r', r === null ? 'null' : r);"
                        + " var e = nnz.lastError; bot.setVar('code', e ? e.code : 'none');"
                        + " bot.send('went on');",
                    budget
                ),
                Grant("tts.speak"),
                new SlowBridge(5000)
            )
        ).Value;

        r.Outcome.Should().Be(ScriptExecutionOutcome.Success, r.ErrorMessage);
        r.VariablesOut["r"].Should().Be("null");
        r.VariablesOut["code"].Should().Be(ScriptHostErrorCodes.UpstreamFailed);
        r.ChatOutput.Should().Be("went on");
        r.HostWaitMs.Should().BeLessThan(2000);
    }

    [Fact]
    public async Task The_next_successful_call_clears_a_deadline_error()
    {
        JintScriptExecutor sut = new();
        ScriptResourceBudget budget = ScriptResourceBudget.Baseline with
        {
            WallClockMs = 30_000,
            MaxHostCallMs = 300,
        };
        int calls = 0;
        IScriptHostBridge bridge = new FirstSlowBridge(() => ++calls);

        ScriptExecutionOutcomeResult r = (
            await sut.ExecuteAsync(
                Request(
                    "bot.call('tts.speak', 'one'); var second = bot.call('tts.speak', 'two');"
                        + " bot.setVar('second', second); bot.setVar('error', nnz.lastError === null ? 'none' : 'set');",
                    budget
                ),
                Grant("tts.speak"),
                bridge
            )
        ).Value;

        r.Outcome.Should().Be(ScriptExecutionOutcome.Success, r.ErrorMessage);
        r.VariablesOut["second"].Should().Be("ok");
        r.VariablesOut["error"].Should().Be("none");
    }

    [Fact]
    public async Task The_run_time_excludes_the_host_call_wait_but_keeps_the_script_sleep()
    {
        JintScriptExecutor sut = new();
        ScriptResourceBudget budget = ScriptResourceBudget.Baseline with { WallClockMs = 30_000 };

        ScriptExecutionOutcomeResult r = (
            await sut.ExecuteAsync(
                Request("bot.call('tts.speak', 'hi'); nnz.time.sleep(300);", budget),
                Grant("tts.speak"),
                new SlowBridge(2000)
            )
        ).Value;

        r.Outcome.Should().Be(ScriptExecutionOutcome.Success, r.ErrorMessage);
        r.HostWaitMs.Should().BeGreaterThanOrEqualTo(1900);
        r.ElapsedMs.Should().BeGreaterThanOrEqualTo(280).And.BeLessThan(1900);
    }

    // The first call waits past any small deadline; every later call answers at once.
    private sealed class FirstSlowBridge(Func<int> count) : IScriptHostBridge
    {
        public HostImportDelegate Resolve(string capabilityKey) =>
            capabilityKey == ScriptHostErrorCodes.LastErrorKey
                ? (_, _, _) => null
                : (_, _, ct) =>
                {
                    if (count() == 1)
                        Task.Delay(5000, ct).GetAwaiter().GetResult();
                    return "ok";
                };
    }
}
