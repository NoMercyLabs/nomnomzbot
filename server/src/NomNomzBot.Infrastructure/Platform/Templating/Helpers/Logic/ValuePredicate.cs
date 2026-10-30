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

using System.Text.RegularExpressions;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Logic;

/// <summary>
/// The operator table of the <c>if.&lt;path&gt;.&lt;op&gt;</c> value form (spec section 6.3.2): string operators
/// compare ordinally (<c>iequals</c> ignores case), the number operators need two numbers, and <c>matches</c> is a
/// regular expression with a time limit.
/// </summary>
internal static class ValuePredicate
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly Dictionary<string, Func<string, string, bool?>> Operators = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["equals"] = (left, operand) => left == operand,
        ["iequals"] = (left, operand) => left.Equals(operand, StringComparison.OrdinalIgnoreCase),
        ["contains"] = (left, operand) => left.Contains(operand, StringComparison.Ordinal),
        ["startswith"] = (left, operand) => left.StartsWith(operand, StringComparison.Ordinal),
        ["endswith"] = (left, operand) => left.EndsWith(operand, StringComparison.Ordinal),
        [RegexOperator] = Matches,
        ["gt"] = (left, operand) => Compare(left, operand, (a, b) => a > b),
        ["lt"] = (left, operand) => Compare(left, operand, (a, b) => a < b),
        ["gte"] = (left, operand) => Compare(left, operand, (a, b) => a >= b),
        ["lte"] = (left, operand) => Compare(left, operand, (a, b) => a <= b),
    };

    /// <summary>The operator that compiles a pattern; each use may cost the full regex timeout.</summary>
    public const string RegexOperator = "matches";

    public static IEnumerable<string> Names => Operators.Keys;

    /// <summary>True for the operator that runs a regular expression, whose cost is not cut by a viewer's count limit.</summary>
    public static bool RunsRegex(string name) =>
        name.Equals(RegexOperator, StringComparison.OrdinalIgnoreCase);

    public static bool IsOperator(string name) => Operators.ContainsKey(name);

    /// <summary>The test result, or null when the operand is not a number (or not a valid pattern) for the operator.</summary>
    public static bool? Evaluate(string name, string left, string operand) =>
        Operators.TryGetValue(name, out Func<string, string, bool?>? test)
            ? test(left, operand)
            : null;

    private static bool? Compare(string left, string operand, Func<double, double, bool> test) =>
        LogicArguments.TryNumber(left, out double a)
        && LogicArguments.TryNumber(operand, out double b)
            ? test(a, b)
            : null;

    private static bool? Matches(string left, string pattern)
    {
        try
        {
            return Regex.IsMatch(left, pattern, RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }
}
