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
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Domain.CustomCode.Enums;
using NomNomzBot.Infrastructure.CustomCode.Jint;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// Proves the self-host sandbox executor (custom-code.md §3.1, code-execution-sandbox.md §4.2): a benign script
/// runs and returns its output/vars; code-from-string (eval + the Function constructor) is blocked; a runaway loop
/// is contained; host calls reach the bridge only for granted capabilities, are denied otherwise, and are bounded
/// by the host-call budget. No fault ever escapes the sandbox — every case returns a value-typed outcome.
/// </summary>
public sealed class JintScriptExecutorTests
{
    private sealed class StubBridge(Func<string, IReadOnlyList<string>, string?> handler)
        : IScriptHostBridge
    {
        public HostImportDelegate Resolve(string capabilityKey) =>
            (key, args, ct) => handler(key, args);
    }

    private static readonly IScriptHostBridge NoBridge = new StubBridge((_, _) => null);

    // A deliberately generous wall clock for the outcome-asserting cases. The 2s production Baseline is a real
    // safety bound, but here it made the OUTCOME race engine construction: the CancellationToken watchdog is
    // armed before the fresh Jint engine + bootstrap are even built, so on a loaded CI box that setup could
    // consume the whole budget before the script's own line ran — flipping an expected Denied/Success/… to
    // Timeout (~1/1676). These scripts finish in microseconds once running, so the large ceiling is never
    // actually consumed; it only removes the race. Cases that assert a resource limit set their own tight
    // budget (A_runaway_loop_is_contained, A_script_that_exceeds_its_wall_clock_is_timed_out).
    private static readonly ScriptResourceBudget Generous = ScriptResourceBudget.Baseline with
    {
        WallClockMs = 30_000,
    };

    private static ScriptExecutionRequest Request(string js, params string[] args) =>
        new(
            "exec-1",
            js,
            "hash",
            new ScriptInputs("u1", "User", args, new Dictionary<string, string>()),
            Generous
        );

    private static ScriptCapabilityGrant Grant(params string[] keys) =>
        new(
            Guid.NewGuid(),
            [.. keys.Select(k => new ScriptCapabilityDescriptor(k, "low", "ff", true))]
        );

    [Fact]
    public async Task Runs_a_benign_script_and_returns_output_and_vars()
    {
        JintScriptExecutor sut = new();

        ScriptExecutionOutcomeResult r = (
            await sut.ExecuteAsync(
                Request("bot.send('hello ' + bot.args[0]); bot.setVar('x', '42');", "world"),
                Grant(),
                NoBridge
            )
        ).Value;

        r.Outcome.Should().Be(ScriptExecutionOutcome.Success);
        r.ChatOutput.Should().Be("hello world");
        r.VariablesOut["x"].Should().Be("42");
    }

    [Fact]
    public async Task Eval_is_blocked()
    {
        JintScriptExecutor sut = new();

        ScriptExecutionOutcomeResult r = (
            await sut.ExecuteAsync(Request("eval('1+1');"), Grant(), NoBridge)
        ).Value;

        r.Outcome.Should().Be(ScriptExecutionOutcome.Faulted);
    }

    [Fact]
    public async Task The_function_constructor_code_from_string_is_blocked()
    {
        JintScriptExecutor sut = new();

        ScriptExecutionOutcomeResult r = (
            await sut.ExecuteAsync(
                Request("(function(){}).constructor('return 1')();"),
                Grant(),
                NoBridge
            )
        ).Value;

        r.Outcome.Should().Be(ScriptExecutionOutcome.Faulted);
    }

    [Fact]
    public async Task A_runaway_loop_is_contained()
    {
        JintScriptExecutor sut = new();

        ScriptExecutionOutcomeResult r = (
            await sut.ExecuteAsync(Request("while (true) {}"), Grant(), NoBridge)
        ).Value;

        r.Outcome.Should().BeOneOf(ScriptExecutionOutcome.Faulted, ScriptExecutionOutcome.Timeout);
    }

    /// <summary>
    /// The wall-clock watchdog maps to <see cref="ScriptExecutionOutcome.Timeout"/>. A busy loop with the
    /// statement cap lifted can ONLY be ended by the clock, so this deterministically times out — it can never
    /// race engine setup (the loop never finishes on its own) and never falls to a statement-overflow fault.
    /// This holds the Timeout coverage the deflaked outcome-cases above deliberately no longer rely on.
    /// </summary>
    [Fact]
    public async Task A_script_that_exceeds_its_wall_clock_is_timed_out()
    {
        JintScriptExecutor sut = new();
        ScriptExecutionRequest request = Request("while (true) {}") with
        {
            Budget = ScriptResourceBudget.Baseline with
            {
                WallClockMs = 250,
                MaxFuelOrStatements = int.MaxValue,
            },
        };

        ScriptExecutionOutcomeResult r = (await sut.ExecuteAsync(request, Grant(), NoBridge)).Value;

        r.Outcome.Should().Be(ScriptExecutionOutcome.Timeout);
    }

    [Fact]
    public async Task A_granted_host_call_reaches_the_bridge()
    {
        JintScriptExecutor sut = new();
        StubBridge bridge = new((key, _) => key == "chat.send" ? "ok" : null);

        ScriptExecutionOutcomeResult r = (
            await sut.ExecuteAsync(
                Request("bot.setVar('out', bot.call('chat.send', 'hi'));"),
                Grant("chat.send"),
                bridge
            )
        ).Value;

        r.Outcome.Should().Be(ScriptExecutionOutcome.Success);
        r.VariablesOut["out"].Should().Be("ok");
        r.HostCallCount.Should().Be(1);
    }

    [Fact]
    public async Task An_ungranted_capability_is_denied()
    {
        JintScriptExecutor sut = new();

        ScriptExecutionOutcomeResult r = (
            await sut.ExecuteAsync(Request("bot.call('music.queue', 's');"), Grant(), NoBridge)
        ).Value;

        r.Outcome.Should().Be(ScriptExecutionOutcome.Denied);
    }

    [Fact]
    public async Task The_host_call_budget_is_enforced()
    {
        JintScriptExecutor sut = new();
        StubBridge bridge = new((_, _) => "ok");
        ScriptExecutionRequest request = Request(
            "for (var i = 0; i < 5; i++) { bot.call('x', 'a'); }"
        ) with
        {
            Budget = ScriptResourceBudget.Baseline with { MaxHostCalls = 2 },
        };

        ScriptExecutionOutcomeResult r = (
            await sut.ExecuteAsync(request, Grant("x"), bridge)
        ).Value;

        r.Outcome.Should().Be(ScriptExecutionOutcome.HostBudgetExceeded);
    }

    [Fact]
    public async Task Compile_declares_the_capabilities_the_script_calls()
    {
        JintScriptExecutor sut = new();

        ScriptCompilation compilation = (
            await sut.CompileAsync("bot.call('chat.send', 'hi'); bot.call(\"music.queue\", 's');")
        ).Value;

        compilation.DeclaredCapabilities.Should().BeEquivalentTo("chat.send", "music.queue");
    }
}
