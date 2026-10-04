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
/// Proves the range rules of <c>nnz.random.int</c> and <c>nnz.math.randomInt</c> in the real Jint engine with the
/// real bootstrap: a reversed range reaches both ends, and a range with no whole number in it gives NaN.
/// </summary>
public sealed class NnzSdkRandomIntRangeTests
{
    private sealed class NoHostBridge : IScriptHostBridge
    {
        public HostImportDelegate Resolve(string capabilityKey) => (_, _, _) => null;
    }

    private static readonly ScriptResourceBudget Generous = ScriptResourceBudget.Baseline with
    {
        WallClockMs = 30_000,
    };

    private static async Task<IReadOnlyDictionary<string, string>> Run(string js)
    {
        ScriptExecutionRequest request = new(
            "exec-1",
            js,
            "hash",
            new("u1", "User", [], new Dictionary<string, string>()),
            Generous
        );
        ScriptExecutionOutcomeResult result = (
            await new JintScriptExecutor().ExecuteAsync(
                request,
                new(Guid.NewGuid(), []),
                new NoHostBridge()
            )
        ).Value;
        result.Outcome.Should().Be(ScriptExecutionOutcome.Success);
        return result.VariablesOut;
    }

    // Rolls the call 3000 times and records the lowest value, the highest value and every distinct value.
    private static string Rolls(string call) =>
        "var lo = Infinity, hi = -Infinity, seen = {};"
        + "for (var i = 0; i < 3000; i++) { var v = "
        + call
        + "; if (v < lo) lo = v; if (v > hi) hi = v; seen[v] = true; }"
        + "bot.setVar('lo', String(lo)); bot.setVar('hi', String(hi));"
        + "bot.setVar('distinct', String(Object.keys(seen).length));";

    [Theory]
    [InlineData("nnz.random.int(6, 1)")]
    [InlineData("nnz.math.randomInt(6, 1)")]
    [InlineData("nnz.random.int(1, 6)")]
    [InlineData("nnz.random.int(6.7, 0.2)")]
    public async Task A_range_with_the_ends_in_either_order_reaches_both_ends_and_nothing_outside(
        string call
    )
    {
        IReadOnlyDictionary<string, string> vars = await Run(Rolls(call));

        // 3000 rolls of 6 values: missing one has odds of about 1e-233, so the assertions are stable.
        vars["lo"].Should().Be("1");
        vars["hi"].Should().Be("6");
        vars["distinct"].Should().Be("6");
    }

    [Theory]
    [InlineData("nnz.random.int(1.2, 1.8)")]
    [InlineData("nnz.random.int(1.8, 1.2)")]
    [InlineData("nnz.math.randomInt(1.2, 1.8)")]
    public async Task A_range_with_no_whole_number_in_it_gives_NaN(string call)
    {
        IReadOnlyDictionary<string, string> vars = await Run(
            "bot.setVar('r', String(" + call + "));"
        );

        vars["r"].Should().Be("NaN");
    }
}
