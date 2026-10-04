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
/// Proves the <c>nnz</c> SDK looks names up by OWN key only: a name every JS object inherits
/// (<c>constructor</c>, <c>toString</c>, <c>valueOf</c>, <c>__proto__</c>) is never a unit and never a
/// <c>nnz.str.format</c> placeholder. The script runs in the real Jint engine with the real Bootstrap.
/// </summary>
public sealed class NnzSdkInheritedKeyTests
{
    private sealed class NoHostBridge : IScriptHostBridge
    {
        public HostImportDelegate Resolve(string capabilityKey) => (_, _, _) => null;
    }

    private static readonly ScriptResourceBudget Generous = ScriptResourceBudget.Baseline with
    {
        WallClockMs = 30_000,
    };

    private static async Task<string> Eval(string expression)
    {
        ScriptExecutionRequest request = new(
            "exec-1",
            $"bot.setVar('r', String({expression}));",
            "hash",
            new("u1", "User", [], new Dictionary<string, string>()),
            Generous
        );
        ScriptExecutionOutcomeResult r = (
            await new JintScriptExecutor().ExecuteAsync(
                request,
                new(Guid.NewGuid(), []),
                new NoHostBridge()
            )
        ).Value;
        r.Outcome.Should().Be(ScriptExecutionOutcome.Success);
        return r.VariablesOut["r"];
    }

    [Theory]
    [InlineData("5, 'constructor', 'constructor'")]
    [InlineData("5, 'toString', 'c'")]
    [InlineData("1, 'km', 'valueOf'")]
    [InlineData("1, 'hasOwnProperty', 'm'")]
    [InlineData("1, '__proto__', '__proto__'")]
    [InlineData("1, 'm', '__proto__'")]
    public async Task Units_convert_treats_an_inherited_object_key_as_no_unit(string args)
    {
        (await Eval($"nnz.units.convert({args})")).Should().Be("NaN");
    }

    [Fact]
    public async Task Units_convert_still_converts_listed_units()
    {
        (await Eval("nnz.units.convert(1, 'km', 'm')")).Should().Be("1000");
        (await Eval("nnz.units.convert(100, 'c', 'f')")).Should().Be("212");
        (await Eval("nnz.units.convert(1, 'Hour', 'min')")).Should().Be("60");
    }

    [Fact]
    public async Task Str_format_leaves_an_inherited_key_placeholder_as_written()
    {
        (await Eval("nnz.str.format('{constructor}-{name}', { name: 'x' })"))
            .Should()
            .Be("{constructor}-x");
    }
}
