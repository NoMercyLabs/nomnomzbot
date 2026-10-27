// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

/// <summary>
/// The scalar operations behind <c>math.*</c> and the expression evaluator. Each returns null for an input with
/// no result (division by zero, the square root of a negative, a logarithm of zero). An overflow of
/// <see cref="decimal"/> throws and is caught once, by <see cref="NumberHelperGroup"/>.
/// </summary>
internal static class NumberMath
{
    /// <summary>The largest whole exponent that is multiplied out exactly; larger ones go through <see cref="double"/>.</summary>
    private const int ExactPowerLimit = 64;

    private const double DecimalLimit = 7.9e28;

    public static decimal? Divide(decimal a, decimal b) => b == 0 ? null : a / b;

    public static decimal? Modulo(decimal a, decimal b) => b == 0 ? null : a % b;

    public static decimal? Sqrt(decimal value) =>
        value < 0 ? null : FromDouble(Math.Sqrt((double)value));

    public static decimal? Log(decimal value, decimal? logBase)
    {
        if (value <= 0 || logBase is <= 0 or 1)
            return null;

        double result = logBase switch
        {
            null => Math.Log((double)value),
            10 => Math.Log10((double)value),
            2 => Math.Log2((double)value),
            _ => Math.Log((double)value) / Math.Log((double)logBase.Value),
        };
        return FromDouble(result);
    }

    public static decimal? Pow(decimal value, decimal exponent)
    {
        if (exponent == decimal.Truncate(exponent) && Math.Abs(exponent) <= ExactPowerLimit)
            return IntegerPower(value, (int)exponent);

        return FromDouble(Math.Pow((double)value, (double)exponent));
    }

    public static decimal Median(IReadOnlyList<decimal> values)
    {
        List<decimal> sorted = [.. values.Order()];
        int middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static decimal? IntegerPower(decimal value, int exponent)
    {
        if (exponent < 0 && value == 0)
            return null;

        decimal result = 1;
        for (int i = 0; i < Math.Abs(exponent); i++)
            result *= value;
        return exponent < 0 ? 1 / result : result;
    }

    private static decimal? FromDouble(double value) =>
        double.IsFinite(value) && Math.Abs(value) < DecimalLimit ? (decimal)value : null;
}
