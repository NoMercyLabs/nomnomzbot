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
using NodaTime;

namespace NomNomzBot.Infrastructure.Platform.Templating.Dates;

/// <summary>A parsed <c>{date:&lt;source&gt;|&lt;step&gt;|…}</c> expression.</summary>
/// <param name="Source">The source text: <c>now</c>, <c>utc</c>, or a date / date-time value.</param>
/// <param name="Steps">The calculation steps, applied left to right.</param>
/// <param name="Output">The output step; null renders the default <c>yyyy-MM-dd HH:mm</c>.</param>
/// <param name="Culture">A <c>lang:</c> override of the channel culture, or null.</param>
public sealed record DateExpression(
    string Source,
    IReadOnlyList<DateStep> Steps,
    DateOutput? Output,
    CultureInfo? Culture
);

/// <summary>The calendar and clock units a calculation step can move or snap by.</summary>
public enum DateUnit
{
    Year,
    Month,
    Week,
    Day,
    Hour,
    Minute,
    Second,
}

/// <summary>The units a difference can be counted in; <see cref="Breakdown"/> and <see cref="Human"/> mix units.</summary>
public enum SpanUnit
{
    Years,
    Months,
    Weeks,
    Days,
    Hours,
    Minutes,
    Seconds,
    Breakdown,
    Human,
}

/// <summary>One calculation step of a date expression.</summary>
public abstract record DateStep;

/// <summary><c>+1y-2mo</c>: one or more signed amounts, applied left to right.</summary>
public sealed record OffsetStep(IReadOnlyList<(int Amount, DateUnit Unit)> Parts) : DateStep;

/// <summary><c>start:&lt;unit&gt;</c> or <c>end:&lt;unit&gt;</c> for day, week, month or year.</summary>
public sealed record BoundaryStep(bool End, DateUnit Unit) : DateStep;

/// <summary><c>next:&lt;weekday&gt;</c> or <c>prev:&lt;weekday&gt;</c>, strictly after or before.</summary>
public sealed record WeekdayStep(bool Next, IsoDayOfWeek Day) : DateStep;

/// <summary><c>tz:&lt;IANA zone&gt;</c>: the same instant seen in another zone.</summary>
public sealed record ZoneStep(DateTimeZone Zone) : DateStep;

/// <summary>The one output step of a date expression.</summary>
public abstract record DateOutput;

/// <summary><c>format:&lt;pattern&gt;</c>: a .NET custom or standard pattern; <c>[o]</c> is the ordinal day.</summary>
public sealed record FormatOutput(string Pattern) : DateOutput;

/// <summary><c>relative</c>: "3 days ago" or "2 hours from now".</summary>
public sealed record RelativeOutput : DateOutput;

/// <summary><c>ordinal</c>: the ordinal day of the month, "26th".</summary>
public sealed record OrdinalOutput : DateOutput;

/// <summary>
/// <c>until</c>, <c>since</c> (Other is null: compared with now) or <c>diff:&lt;date&gt;</c>, counted in
/// <see cref="Unit"/>.
/// </summary>
public sealed record SpanOutput(string? Other, SpanUnit Unit) : DateOutput;
