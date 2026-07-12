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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Pipeline.CoreActions;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline.CoreActions;

public class ComparisonConditionTests
{
    private static PipelineExecutionContext BuildCtx() =>
        new()
        {
            BroadcasterId = "chan",
            TriggeredByUserId = "user",
            TriggeredByDisplayName = "User",
            MessageId = "msg",
            RawMessage = "",
        };

    /// <summary>
    /// Real <see cref="TemplateResolver"/> (not a stand-in) so template resolution is
    /// genuinely exercised end to end — only its unused DB/channel dependencies are faked.
    /// </summary>
    private static ITemplateResolver CreateResolver()
    {
        IServiceScopeFactory scopeFactory = Substitute.For<IServiceScopeFactory>();
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Arg.Any<string>()).Returns((ChannelContext?)null);

        return new TemplateResolver(
            scopeFactory,
            registry,
            NullLogger<TemplateResolver>.Instance,
            TimeProvider.System
        );
    }

    private static ConditionDefinition MakeCond(string json) =>
        JsonSerializer.Deserialize<ConditionDefinition>(json)!;

    // ─── Numeric comparisons ────────────────────────────────────────────────────

    [Theory]
    [InlineData("10", "eq", "10", true)]
    [InlineData("10", "==", "10", true)]
    [InlineData("10", "eq", "11", false)]
    [InlineData("10", "ne", "11", true)]
    [InlineData("10", "!=", "10", false)]
    [InlineData("15", "gt", "10", true)]
    [InlineData("15", ">", "10", true)]
    [InlineData("5", "gt", "10", false)]
    [InlineData("5", "lt", "10", true)] // numeric: 5 < 10 (lexically "5" > "10")
    [InlineData("5", "<", "10", true)]
    [InlineData("10", "lt", "10", false)]
    [InlineData("10", "gte", "10", true)]
    [InlineData("10", ">=", "9", true)]
    [InlineData("9", "gte", "10", false)]
    [InlineData("10", "lte", "10", true)]
    [InlineData("10", "<=", "11", true)]
    [InlineData("11", "lte", "10", false)]
    [InlineData("10.1", "gt", "9.9", true)] // numeric: 10.1 > 9.9 (lexically "10.1" < "9.9")
    [InlineData("-3", "lt", "0", true)]
    public async Task EvaluateAsync_NumericOperators_CompareAsNumbers(
        string left,
        string op,
        string right,
        bool expected
    )
    {
        ComparisonCondition condition = new(CreateResolver());
        PipelineExecutionContext ctx = BuildCtx();
        ConditionDefinition def = MakeCond(
            $$"""{"type":"comparison","left":"{{left}}","operator":"{{op}}","right":"{{right}}"}"""
        );

        bool result = await condition.EvaluateAsync(ctx, def);

        result.Should().Be(expected);
    }

    // ─── String comparisons ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("hello", "eq", "HELLO", true)]
    [InlineData("hello", "eq", "world", false)]
    [InlineData("hello", "ne", "world", true)]
    [InlineData("hello", "ne", "HELLO", false)]
    public async Task EvaluateAsync_StringEquality_IsCaseInsensitive(
        string left,
        string op,
        string right,
        bool expected
    )
    {
        ComparisonCondition condition = new(CreateResolver());
        PipelineExecutionContext ctx = BuildCtx();
        ConditionDefinition def = MakeCond(
            $$"""{"type":"comparison","left":"{{left}}","operator":"{{op}}","right":"{{right}}"}"""
        );

        bool result = await condition.EvaluateAsync(ctx, def);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("hello world", "contains", "WORLD", true)]
    [InlineData("hello world", "contains", "xyz", false)]
    [InlineData("hello world", "starts_with", "Hello", true)]
    [InlineData("hello world", "starts_with", "world", false)]
    [InlineData("hello world", "ends_with", "WORLD", true)]
    [InlineData("hello world", "ends_with", "hello", false)]
    public async Task EvaluateAsync_StringOperators_AreCaseInsensitive(
        string left,
        string op,
        string right,
        bool expected
    )
    {
        ComparisonCondition condition = new(CreateResolver());
        PipelineExecutionContext ctx = BuildCtx();
        ConditionDefinition def = MakeCond(
            $$"""{"type":"comparison","left":"{{left}}","operator":"{{op}}","right":"{{right}}"}"""
        );

        bool result = await condition.EvaluateAsync(ctx, def);

        result.Should().Be(expected);
    }

    // ─── Type-coercion boundary ──────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_OneSideNonNumeric_FallsBackToStringCompareOnBothSides()
    {
        // "10" alone would parse as a number, but "abc" does not — since it is NOT
        // true that BOTH sides parse, the whole comparison must fall back to a
        // case-insensitive STRING compare rather than throw or silently coerce.
        ComparisonCondition condition = new(CreateResolver());
        PipelineExecutionContext ctx = BuildCtx();
        ConditionDefinition def = MakeCond(
            """{"type":"comparison","left":"10","operator":"eq","right":"abc"}"""
        );

        bool result = await condition.EvaluateAsync(ctx, def);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_BothSidesNumeric_ComparesNumericallyNotLexically()
    {
        // Lexically "9" > "10" (character '9' > '1'), but 9 < 10 numerically.
        // Proves numeric parsing wins whenever both operands parse as numbers.
        ComparisonCondition condition = new(CreateResolver());
        PipelineExecutionContext ctx = BuildCtx();
        ConditionDefinition def = MakeCond(
            """{"type":"comparison","left":"9","operator":"lt","right":"10"}"""
        );

        bool result = await condition.EvaluateAsync(ctx, def);

        result.Should().BeTrue();
    }

    // ─── Template-resolved operands ──────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_LeftIsTemplateVariable_ResolvesFromContextBeforeComparing()
    {
        ComparisonCondition condition = new(CreateResolver());
        PipelineExecutionContext ctx = BuildCtx();
        ctx.Variables["count.wins"] = "42";
        ConditionDefinition def = MakeCond(
            """{"type":"comparison","left":"{count.wins}","operator":"gte","right":"42"}"""
        );

        bool result = await condition.EvaluateAsync(ctx, def);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_BothOperandsAreTemplates_ResolvesBothBeforeComparing()
    {
        ComparisonCondition condition = new(CreateResolver());
        PipelineExecutionContext ctx = BuildCtx();
        ctx.Variables["args.0"] = "raid";
        ctx.Variables["target"] = "raid";
        ConditionDefinition def = MakeCond(
            """{"type":"comparison","left":"{args.0}","operator":"eq","right":"{target}"}"""
        );

        bool result = await condition.EvaluateAsync(ctx, def);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_SameDefinitionDifferentContextState_ChangesOutcome()
    {
        // One immutable ConditionDefinition, evaluated against two different execution
        // contexts — the pass/fail must track the live variable value, proving the
        // comparison is driven by resolution, not by anything baked into the definition.
        ComparisonCondition condition = new(CreateResolver());
        ConditionDefinition def = MakeCond(
            """{"type":"comparison","left":"{count.wins}","operator":"gt","right":"10"}"""
        );

        PipelineExecutionContext lowCtx = BuildCtx();
        lowCtx.Variables["count.wins"] = "5";
        bool lowResult = await condition.EvaluateAsync(lowCtx, def);

        PipelineExecutionContext highCtx = BuildCtx();
        highCtx.Variables["count.wins"] = "50";
        bool highResult = await condition.EvaluateAsync(highCtx, def);

        lowResult.Should().BeFalse();
        highResult.Should().BeTrue();
    }

    // ─── Safe-fail on malformed input ────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_MissingLeftOperand_ReturnsFalseWithoutThrowing()
    {
        ComparisonCondition condition = new(CreateResolver());
        PipelineExecutionContext ctx = BuildCtx();
        ConditionDefinition def = MakeCond("""{"type":"comparison","operator":"eq","right":"5"}""");

        bool result = await condition.EvaluateAsync(ctx, def);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_BlankLeftOperand_ReturnsFalseWithoutThrowing()
    {
        ComparisonCondition condition = new(CreateResolver());
        PipelineExecutionContext ctx = BuildCtx();
        ConditionDefinition def = MakeCond(
            """{"type":"comparison","left":"   ","operator":"eq","right":"5"}"""
        );

        bool result = await condition.EvaluateAsync(ctx, def);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_MissingOperator_ReturnsFalseWithoutThrowing()
    {
        ComparisonCondition condition = new(CreateResolver());
        PipelineExecutionContext ctx = BuildCtx();
        ConditionDefinition def = MakeCond("""{"type":"comparison","left":"5","right":"5"}""");

        bool result = await condition.EvaluateAsync(ctx, def);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_UnknownOperator_ReturnsFalseWithoutThrowing()
    {
        ComparisonCondition condition = new(CreateResolver());
        PipelineExecutionContext ctx = BuildCtx();
        ConditionDefinition def = MakeCond(
            """{"type":"comparison","left":"5","operator":"frobnicate","right":"5"}"""
        );

        bool result = await condition.EvaluateAsync(ctx, def);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_MissingRightOperand_ComparesAgainstEmptyString()
    {
        // Right is optional structurally: a missing "right" key defaults to comparing
        // against an empty string, which is itself a well-defined, non-throwing result.
        ComparisonCondition condition = new(CreateResolver());
        PipelineExecutionContext ctx = BuildCtx();
        ConditionDefinition def = MakeCond(
            """{"type":"comparison","left":"hello","operator":"eq"}"""
        );

        bool result = await condition.EvaluateAsync(ctx, def);

        result.Should().BeFalse();
    }

    // ─── Contract ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ConditionType_IsComparison()
    {
        ComparisonCondition condition = new(CreateResolver());
        condition.ConditionType.Should().Be("comparison");
    }
}
