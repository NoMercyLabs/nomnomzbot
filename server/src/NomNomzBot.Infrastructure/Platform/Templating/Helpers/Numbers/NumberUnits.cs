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

/// <summary>The fixed unit table of <c>num.convert</c>: every unit knows its dimension and its size in the dimension's base unit.</summary>
internal static class NumberUnits
{
    /// <summary>The quantity a unit measures.</summary>
    public enum Dimension
    {
        Length,
        Mass,
        Volume,
        Temperature,
    }

    private sealed record Unit(Dimension Dimension, decimal PerBase);

    private static readonly Dictionary<string, Unit> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        // Length, base metre.
        ["mm"] = new(Dimension.Length, 0.001m),
        ["cm"] = new(Dimension.Length, 0.01m),
        ["m"] = new(Dimension.Length, 1m),
        ["km"] = new(Dimension.Length, 1000m),
        ["in"] = new(Dimension.Length, 0.0254m),
        ["ft"] = new(Dimension.Length, 0.3048m),
        ["yd"] = new(Dimension.Length, 0.9144m),
        ["mi"] = new(Dimension.Length, 1609.344m),
        // Mass, base kilogram.
        ["g"] = new(Dimension.Mass, 0.001m),
        ["kg"] = new(Dimension.Mass, 1m),
        ["oz"] = new(Dimension.Mass, 0.028349523125m),
        ["lb"] = new(Dimension.Mass, 0.45359237m),
        // Volume, base litre (US gallon and quart).
        ["ml"] = new(Dimension.Volume, 0.001m),
        ["l"] = new(Dimension.Volume, 1m),
        ["qt"] = new(Dimension.Volume, 0.946352946m),
        ["gal"] = new(Dimension.Volume, 3.785411784m),
        // Temperature has no ratio; it is converted through Celsius.
        ["c"] = new(Dimension.Temperature, 1m),
        ["f"] = new(Dimension.Temperature, 1m),
        ["k"] = new(Dimension.Temperature, 1m),
    };

    /// <summary>The value in the target unit; false when a unit is unknown or the two measure different things.</summary>
    public static bool TryConvert(decimal value, string from, string to, out decimal result)
    {
        result = 0;
        if (
            !Table.TryGetValue(from, out Unit? source)
            || !Table.TryGetValue(to, out Unit? target)
            || source.Dimension != target.Dimension
        )
            return false;

        result =
            source.Dimension == Dimension.Temperature
                ? FromCelsius(ToCelsius(value, from), to)
                : value * source.PerBase / target.PerBase;
        return true;
    }

    /// <summary>A length as whole feet and rounded inches, <c>5'11"</c>; false for a negative or non-length input.</summary>
    public static bool TryFeetAndInches(decimal value, string from, out string text)
    {
        text = string.Empty;
        if (!TryConvert(value, from, "in", out decimal inches) || inches < 0)
            return false;

        decimal feet = decimal.Truncate(inches / 12);
        decimal rest = NumberText.Round(inches - feet * 12, 0);
        if (rest == 12)
        {
            feet++;
            rest = 0;
        }

        text = $"{NumberText.Plain(feet)}'{NumberText.Plain(rest)}\"";
        return true;
    }

    private static decimal ToCelsius(decimal value, string unit) =>
        unit.ToLowerInvariant() switch
        {
            "f" => (value - 32) * 5 / 9,
            "k" => value - 273.15m,
            _ => value,
        };

    private static decimal FromCelsius(decimal celsius, string unit) =>
        unit.ToLowerInvariant() switch
        {
            "f" => celsius * 9 / 5 + 32,
            "k" => celsius + 273.15m,
            _ => celsius,
        };
}
