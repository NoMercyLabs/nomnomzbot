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
using NomNomzBot.Infrastructure.DevPlatform;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// <c>nnz.random.pick</c> always returns an item of the list, so its type is <c>T</c> and a script needs no
/// <c>!</c>. An empty list is a script bug: the run stops with an error that names the call.
/// </summary>
public sealed class NnzSdkRandomPickTests
{
    private sealed class NoHostBridge : IScriptHostBridge
    {
        public HostImportDelegate Resolve(string capabilityKey) => (_, _, _) => null;
    }

    private static async Task<ScriptExecutionOutcomeResult> Run(string js)
    {
        ScriptExecutionRequest request = new(
            "exec-1",
            js,
            "hash",
            new("u1", "User", [], new Dictionary<string, string>()),
            ScriptResourceBudget.Baseline with
            {
                WallClockMs = 30_000,
            }
        );
        return (
            await new JintScriptExecutor().ExecuteAsync(
                request,
                new(Guid.NewGuid(), []),
                new NoHostBridge()
            )
        ).Value;
    }

    [Fact]
    public async Task An_empty_list_stops_the_script_with_an_error_that_names_the_call()
    {
        ScriptExecutionOutcomeResult result = await Run(
            "bot.setVar('r', String(nnz.random.pick([])));"
        );

        result.Outcome.Should().Be(ScriptExecutionOutcome.Faulted);
        result.ErrorMessage.Should().Contain("nnz.random.pick needs a list with at least one item");
        result.VariablesOut.Should().NotContainKey("r");
    }

    [Fact]
    public async Task A_list_gives_one_of_its_own_items_and_every_item_can_come_up()
    {
        ScriptExecutionOutcomeResult result = await Run(
            "var seen = {}; for (var i = 0; i < 600; i++) { seen[nnz.random.pick(['a', 'b', 'c'])] = true; }"
                + "bot.setVar('seen', Object.keys(seen).sort().join(','));"
        );

        result.Outcome.Should().Be(ScriptExecutionOutcome.Success);
        result.VariablesOut["seen"].Should().Be("a,b,c");
    }

    [Fact]
    public void The_declared_type_returns_the_item_type_without_undefined()
    {
        string declarations = SdkRuntimeSurface.ScriptGlobals();

        declarations.Should().Contain("pick<T>(items: readonly T[]): T;");
        declarations.Should().NotContain("pick<T>(items: readonly T[]): T | undefined;");
    }
}
