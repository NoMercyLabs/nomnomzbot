// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Pipeline.CoreActions;

/// <summary>
/// Condition: compare two operands. Both operands are resolved as templates first
/// (the same <see cref="ITemplateResolver"/> path <c>send_message</c> uses), so
/// variables such as <c>{count.wins}</c>, <c>{args.0}</c> work as either side.
/// Numeric comparison is used when both resolved operands parse as numbers
/// (invariant culture); otherwise the comparison falls back to a case-insensitive
/// string comparison. <c>contains</c>/<c>starts_with</c>/<c>ends_with</c> are always
/// string operations.
///
/// Usage: { "type": "comparison", "left": "{count.wins}", "operator": "gt", "right": "10" }
///        { "type": "comparison", "left": "{args.0}", "operator": "contains", "right": "raid" }
///
/// Supported operators (case-insensitive, symbolic and word forms both accepted):
///   eq / ==, ne / !=, gt / &gt;, lt / &lt;, gte / &gt;=, lte / &lt;=,
///   contains, starts_with, ends_with
///
/// Never throws on malformed input — a missing operator, a missing left operand, or
/// an unrecognized operator all fail the condition (return false) rather than throw.
/// </summary>
public sealed class ComparisonCondition : ICommandCondition
{
    private readonly ITemplateResolver _resolver;

    public string ConditionType => "comparison";

    public ComparisonCondition(ITemplateResolver resolver)
    {
        _resolver = resolver;
    }

    public async Task<bool> EvaluateAsync(
        PipelineExecutionContext ctx,
        ConditionDefinition condition
    )
    {
        string? operatorToken = condition.GetString("operator");
        string? leftTemplate = condition.GetString("left");
        string? rightTemplate = condition.GetString("right");

        // A comparison with no operator or no left-hand side is malformed — fail safe.
        if (string.IsNullOrWhiteSpace(operatorToken) || string.IsNullOrWhiteSpace(leftTemplate))
            return false;

        if (!TryParseOperator(operatorToken, out Operator op))
            return false;

        string left = await _resolver.ResolveAsync(
            leftTemplate,
            ctx.Variables,
            ctx.BroadcasterId,
            ctx.CancellationToken
        );
        string right = await _resolver.ResolveAsync(
            rightTemplate ?? string.Empty,
            ctx.Variables,
            ctx.BroadcasterId,
            ctx.CancellationToken
        );

        return op switch
        {
            Operator.Contains => left.Contains(right, StringComparison.OrdinalIgnoreCase),
            Operator.StartsWith => left.StartsWith(right, StringComparison.OrdinalIgnoreCase),
            Operator.EndsWith => left.EndsWith(right, StringComparison.OrdinalIgnoreCase),
            _ => EvaluateRelational(left, right, op),
        };
    }

    // ─── Relational comparison ─────────────────────────────────────────────────

    private static bool EvaluateRelational(string left, string right, Operator op)
    {
        int comparison =
            TryParseNumber(left, out double leftNumber)
            && TryParseNumber(right, out double rightNumber)
                ? leftNumber.CompareTo(rightNumber)
                : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);

        return op switch
        {
            Operator.Equal => comparison == 0,
            Operator.NotEqual => comparison != 0,
            Operator.GreaterThan => comparison > 0,
            Operator.LessThan => comparison < 0,
            Operator.GreaterOrEqual => comparison >= 0,
            Operator.LessOrEqual => comparison <= 0,
            _ => false,
        };
    }

    private static bool TryParseNumber(string value, out double number) =>
        double.TryParse(
            value,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out number
        );

    // ─── Operator parsing ──────────────────────────────────────────────────────

    private enum Operator
    {
        Equal,
        NotEqual,
        GreaterThan,
        LessThan,
        GreaterOrEqual,
        LessOrEqual,
        Contains,
        StartsWith,
        EndsWith,
    }

    private static bool TryParseOperator(string raw, out Operator op)
    {
        switch (raw.Trim().ToLowerInvariant())
        {
            case "eq":
            case "==":
                op = Operator.Equal;
                return true;
            case "ne":
            case "!=":
                op = Operator.NotEqual;
                return true;
            case "gt":
            case ">":
                op = Operator.GreaterThan;
                return true;
            case "lt":
            case "<":
                op = Operator.LessThan;
                return true;
            case "gte":
            case ">=":
                op = Operator.GreaterOrEqual;
                return true;
            case "lte":
            case "<=":
                op = Operator.LessOrEqual;
                return true;
            case "contains":
                op = Operator.Contains;
                return true;
            case "starts_with":
                op = Operator.StartsWith;
                return true;
            case "ends_with":
                op = Operator.EndsWith;
                return true;
            default:
                op = default;
                return false;
        }
    }
}
