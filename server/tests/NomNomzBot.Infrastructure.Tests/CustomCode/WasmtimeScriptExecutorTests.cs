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
using Microsoft.Extensions.Configuration;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Domain.CustomCode.Enums;
using NomNomzBot.Infrastructure.CustomCode.Wasmtime;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// Proves the SaaS Wasmtime executor's security harness (code-execution-sandbox.md §4.1) against real wasm: a
/// benign module runs to completion, but a runaway loop is fuel-bounded and trapped (contained, not killing the
/// host) — the core isolation control. Run-time JS dispatch fails closed until the engine module is deployed, and
/// save-time capability declaration works. (The hardened Engine/Config — Cranelift-only, risky proposals off — is
/// asserted by construction; the guest-containment is asserted dynamically here.)
/// </summary>
public sealed class WasmtimeScriptExecutorTests
{
    private static WasmtimeScriptExecutor Build() => new(Substitute.For<IConfiguration>());

    [Fact]
    public void Runtime_is_wasmtime()
    {
        using WasmtimeScriptExecutor sut = Build();
        sut.Runtime.Should().Be(ScriptRuntimeKind.Wasmtime);
    }

    [Fact]
    public void A_benign_module_runs_to_completion()
    {
        using WasmtimeScriptExecutor sut = Build();

        ScriptExecutionOutcome outcome = sut.RunHardenedModule(
            "(module (func (export \"run\") nop))",
            "run",
            fuel: 100_000
        );

        outcome.Should().Be(ScriptExecutionOutcome.Success);
    }

    [Fact]
    public void A_runaway_loop_is_fuel_bounded_and_contained()
    {
        using WasmtimeScriptExecutor sut = Build();

        ScriptExecutionOutcome outcome = sut.RunHardenedModule(
            "(module (func (export \"run\") (loop $l (br $l))))",
            "run",
            fuel: 1_000
        );

        outcome.Should().Be(ScriptExecutionOutcome.Timeout); // fuel exhausted → trapped, host unharmed
    }

    [Fact]
    public async Task ExecuteAsync_without_an_engine_module_fails_closed()
    {
        using WasmtimeScriptExecutor sut = Build();
        ScriptExecutionRequest request = new(
            "exec-1",
            "bot.send('hi');",
            "hash",
            new ScriptInputs("u1", "User", [], new Dictionary<string, string>()),
            ScriptResourceBudget.Baseline
        );

        ScriptExecutionOutcomeResult result = (
            await sut.ExecuteAsync(
                request,
                new ScriptCapabilityGrant(Guid.NewGuid(), []),
                Substitute.For<IScriptHostBridge>()
            )
        ).Value;

        result.Outcome.Should().Be(ScriptExecutionOutcome.Faulted);
    }

    [Fact]
    public async Task Compile_declares_the_capabilities_the_script_calls()
    {
        using WasmtimeScriptExecutor sut = Build();

        ScriptCompilation compilation = (
            await sut.CompileAsync("bot.call('chat.send', 'hi');")
        ).Value;

        compilation.DeclaredCapabilities.Should().Contain("chat.send");
    }
}
