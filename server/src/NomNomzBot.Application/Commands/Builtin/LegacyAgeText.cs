// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Commands.Builtin;

/// <summary>
/// The duration wording of the old bot's <c>!accountage</c> and <c>!followage</c> replies. The two commands
/// word the same span differently (legacy parity), so each keeps its own format.
/// </summary>
public static class LegacyAgeText
{
    /// <summary>"1 year, 3 months, 2 days" — the old <c>!accountage</c> wording (comma-joined parts).</summary>
    public static string AccountAge(TimeSpan span)
    {
        if (span.TotalDays >= 365)
        {
            int years = (int)(span.TotalDays / 365);
            int remainingDays = (int)(span.TotalDays % 365);
            string text = Plural(years, "year");
            return text
                + Remainder(remainingDays / 30, "month")
                + Remainder(remainingDays % 30, "day");
        }

        if (span.TotalDays >= 30)
        {
            int months = (int)(span.TotalDays / 30);
            return Plural(months, "month") + Remainder((int)(span.TotalDays % 30), "day");
        }

        return ShortSpan(span);
    }

    /// <summary>"1 year and 20 days" — the old <c>!followage</c> wording (the remainder is always days).</summary>
    public static string FollowAge(TimeSpan span)
    {
        if (span.TotalDays >= 365)
        {
            int years = (int)(span.TotalDays / 365);
            return Plural(years, "year") + AndDays((int)(span.TotalDays % 365));
        }

        if (span.TotalDays >= 30)
        {
            int months = (int)(span.TotalDays / 30);
            return Plural(months, "month") + AndDays((int)(span.TotalDays % 30));
        }

        return ShortSpan(span);
    }

    private static string ShortSpan(TimeSpan span)
    {
        if (span.TotalDays >= 1)
            return Plural((int)span.TotalDays, "day");
        if (span.TotalHours >= 1)
            return Plural((int)span.TotalHours, "hour");
        return Plural(Math.Max(1, (int)span.TotalMinutes), "minute");
    }

    private static string Plural(int count, string unit) =>
        count == 1 ? $"1 {unit}" : $"{count} {unit}s";

    private static string Remainder(int count, string unit) =>
        count > 0 ? $", {Plural(count, unit)}" : string.Empty;

    private static string AndDays(int days) =>
        days > 0 ? $" and {Plural(days, "day")}" : string.Empty;
}
