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
using System.Text.RegularExpressions;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// Caps the sizes a chat-HTML sender may ask for, so one message can never cover the overlay. Runs on markup the
/// sanitiser already reduced to its CSS allow-list: an inline style keeps each allowed declaration, with any length
/// over its cap pulled down to the cap; a <c>width</c>/<c>height</c> attribute is read the way a browser reads it
/// (<c>500px</c> and <c>500p</c> both mean 500) and capped the same way.
/// </summary>
public static partial class ChatHtmlSizeLimits
{
    private const double MaxWidthPx = 500;
    private const double MaxHeightPx = 300;

    // Per property: the largest px, em/rem and % value, and the largest unitless number (0 = a unitless value other
    // than zero is not a length for that property). A value the cap cannot read (vw, calc(), !important) is dropped.
    private static readonly Dictionary<string, LengthCap> LengthCaps = new()
    {
        ["width"] = new(MaxWidthPx, 30, 100, 0),
        ["max-width"] = new(MaxWidthPx, 30, 100, 0),
        ["height"] = new(MaxHeightPx, 18, 100, 0),
        ["max-height"] = new(MaxHeightPx, 18, 100, 0),
        ["font-size"] = new(48, 3, 300, 0),
        ["line-height"] = new(72, 3, 300, 3),
        ["letter-spacing"] = new(10, 0.5, 0, 0),
    };

    private static readonly HashSet<string> LengthKeywords = ["auto", "normal"];

    // What a shorthand like "background: url(x)" leaves behind once its url part is gone; it styles nothing.
    private static readonly HashSet<string> CssWideKeywords =
    [
        "initial",
        "inherit",
        "unset",
        "revert",
    ];

    /// <summary>
    /// The inline style with every length capped and every declaration the caps cannot vouch for removed; null when
    /// nothing is left.
    /// </summary>
    public static string? LimitStyle(string style, IReadOnlySet<string> allowedProperties)
    {
        List<string> kept = [];
        foreach (string declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = declaration.IndexOf(':');
            if (colon < 0)
                continue;

            string property = declaration[..colon].Trim().ToLowerInvariant();
            string value = declaration[(colon + 1)..].Trim();
            if (!allowedProperties.Contains(property) || !IsAllowedValue(value))
                continue;

            string? limited = LengthCaps.TryGetValue(property, out LengthCap cap)
                ? LimitLength(value, cap)
                : value;
            if (limited is not null)
                kept.Add($"{property}: {limited}");
        }

        return kept.Count == 0 ? null : string.Join("; ", kept);
    }

    /// <summary>A <c>width</c>/<c>height</c> attribute value as a capped number (or percentage); null to drop it.</summary>
    public static string? LimitDimensionAttribute(string attribute, string value)
    {
        Match match = LeadingNumber().Match(value);
        if (!match.Success)
            return null;

        double number = double.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
        if (match.Groups["pct"].Success)
            return Format(Math.Min(number, 100)) + "%";

        double max = attribute == "height" ? MaxHeightPx : MaxWidthPx;
        return Format(Math.Floor(Math.Min(number, max)));
    }

    private static bool IsAllowedValue(string value) =>
        !CssWideKeywords.Contains(value.ToLowerInvariant())
        && !value.Contains("url(", StringComparison.OrdinalIgnoreCase)
        && !value.Contains("expression", StringComparison.OrdinalIgnoreCase)
        && !value.Contains('\\')
        && !value.Contains('!');

    private static string? LimitLength(string value, LengthCap cap)
    {
        string lower = value.ToLowerInvariant();
        if (LengthKeywords.Contains(lower))
            return lower;

        Match match = Length().Match(lower);
        if (!match.Success)
            return null;

        double number = double.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
        string unit = match.Groups["unit"].Value;
        double max = unit switch
        {
            "px" => cap.Px,
            "em" or "rem" => cap.Em,
            "%" => cap.Percent,
            _ => number == 0 ? 0 : cap.Unitless,
        };
        if (max == 0 && number != 0)
            return null;

        double limited = Math.Sign(number) * Math.Min(Math.Abs(number), max);
        return Format(limited) + unit;
    }

    private static string Format(double number) =>
        number.ToString("0.###", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(?<n>-?\d+(?:\.\d+)?)(?<unit>px|em|rem|%)?$")]
    private static partial Regex Length();

    [GeneratedRegex(@"^\s*(?<n>\d+(?:\.\d+)?)(?<pct>%)?")]
    private static partial Regex LeadingNumber();

    private readonly record struct LengthCap(double Px, double Em, double Percent, double Unitless);
}
