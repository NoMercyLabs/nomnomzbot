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
using NodaTime;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Platform.Templating.Dates;

/// <summary>
/// Parses the payload of <c>{date:&lt;source&gt;|&lt;step&gt;|…|&lt;output&gt;}</c> into a
/// <see cref="DateExpression"/>. Steps are split on <c>|</c>; the output step, when present, must be last.
/// Every mistake is a failure with a message naming the step; nothing here throws.
/// </summary>
public static partial class DateExpressionParser
{
    private const string ErrorCode = "TEMPLATE_INVALID_DATE_EXPRESSION";

    public static Result<DateExpression> Parse(string payload)
    {
        string[] parts = payload.Split('|');
        string source = parts[0].Trim();
        if (source.Length == 0)
            return Fail("the date source is empty");

        List<DateStep> steps = [];
        DateOutput? output = null;
        CultureInfo? culture = null;

        foreach (string raw in parts.Skip(1))
        {
            string step = raw.Trim();
            if (output is not null)
                return Fail($"'{step}' comes after the output step; the output step must be last");

            (string name, string? argument) = SplitStep(step);
            Result<bool> handled = name.ToLowerInvariant() switch
            {
                "lang" => ParseLanguage(argument, ref culture),
                _ when IsOutput(name) => ParseOutput(name, argument, ref output),
                _ => ParseStep(step, name, argument, steps),
            };
            if (handled.IsFailure)
                return Result<DateExpression>.Failure(handled.ErrorMessage!, ErrorCode);
        }

        return Result<DateExpression>.Success(new DateExpression(source, steps, output, culture));
    }

    private static (string Name, string? Argument) SplitStep(string step)
    {
        int colon = step.IndexOf(':');
        return colon < 0 ? (step, null) : (step[..colon].Trim(), step[(colon + 1)..]);
    }

    private static bool IsOutput(string name) =>
        name.ToLowerInvariant()
            is "format"
                or "relative"
                or "ordinal"
                or "until"
                or "since"
                or "diff";

    private static Result<bool> ParseStep(
        string step,
        string name,
        string? argument,
        List<DateStep> steps
    )
    {
        DateStep? parsed = name.ToLowerInvariant() switch
        {
            "start" => ParseBoundary(end: false, argument),
            "end" => ParseBoundary(end: true, argument),
            "next" => ParseWeekday(next: true, argument),
            "prev" => ParseWeekday(next: false, argument),
            "tz" => ParseZone(argument),
            _ => ParseOffset(step),
        };
        if (parsed is null)
            return Fail<bool>($"'{step}' is not a date step");

        steps.Add(parsed);
        return Result<bool>.Success(true);
    }

    private static OffsetStep? ParseOffset(string step)
    {
        if (!OffsetPattern().IsMatch(step))
            return null;

        List<(int Amount, DateUnit Unit)> parts = [];
        foreach (Match match in OffsetPartPattern().Matches(step))
        {
            if (!int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out int amount))
                return null;
            parts.Add((amount, ParseOffsetUnit(match.Groups[2].Value)));
        }
        return new OffsetStep(parts);
    }

    private static DateUnit ParseOffsetUnit(string unit) =>
        unit.ToLowerInvariant() switch
        {
            "y" => DateUnit.Year,
            "mo" => DateUnit.Month,
            "w" => DateUnit.Week,
            "d" => DateUnit.Day,
            "h" => DateUnit.Hour,
            "m" => DateUnit.Minute,
            _ => DateUnit.Second,
        };

    private static BoundaryStep? ParseBoundary(bool end, string? argument)
    {
        DateUnit? unit = argument?.Trim().ToLowerInvariant() switch
        {
            "day" => DateUnit.Day,
            "week" => DateUnit.Week,
            "month" => DateUnit.Month,
            "year" => DateUnit.Year,
            _ => null,
        };
        return unit is null ? null : new BoundaryStep(end, unit.Value);
    }

    private static WeekdayStep? ParseWeekday(bool next, string? argument)
    {
        string name = argument?.Trim() ?? string.Empty;
        if (name.Length < 3)
            return null;

        foreach (IsoDayOfWeek day in Enum.GetValues<IsoDayOfWeek>())
        {
            if (
                day != IsoDayOfWeek.None
                && day.ToString().StartsWith(name, StringComparison.OrdinalIgnoreCase)
            )
                return new WeekdayStep(next, day);
        }
        return null;
    }

    private static ZoneStep? ParseZone(string? argument)
    {
        DateTimeZone? zone = DateExpressionContext.FindZone(argument);
        return zone is null ? null : new ZoneStep(zone);
    }

    private static Result<bool> ParseLanguage(string? argument, ref CultureInfo? culture)
    {
        culture = DateExpressionContext.FindCulture(argument);
        return culture is null
            ? Fail<bool>($"'{argument}' is not a known language")
            : Result<bool>.Success(true);
    }

    private static Result<bool> ParseOutput(string name, string? argument, ref DateOutput? output)
    {
        output = name.ToLowerInvariant() switch
        {
            "format" => string.IsNullOrEmpty(argument) ? null : new FormatOutput(argument),
            "relative" => argument is null ? new RelativeOutput() : null,
            "ordinal" => argument is null ? new OrdinalOutput() : null,
            "until" or "since" => ParseNowSpan(argument),
            _ => ParseDiff(argument),
        };
        return output is null
            ? Fail<bool>($"'{name}:{argument}' is not a valid output step")
            : Result<bool>.Success(true);
    }

    private static SpanOutput? ParseNowSpan(string? argument)
    {
        if (argument is null)
            return new SpanOutput(null, SpanUnit.Human);
        SpanUnit? unit = ParseSpanUnit(argument);
        return unit is null ? null : new SpanOutput(null, unit.Value);
    }

    /// <summary>
    /// <c>diff:&lt;date&gt;[:&lt;unit&gt;]</c>. The other date may hold colons itself (a time), so the unit is
    /// only split off when the text after the last colon is a known unit.
    /// </summary>
    private static SpanOutput? ParseDiff(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return null;

        int lastColon = argument.LastIndexOf(':');
        if (lastColon > 0)
        {
            SpanUnit? unit = ParseSpanUnit(argument[(lastColon + 1)..]);
            if (unit is not null)
                return new SpanOutput(argument[..lastColon].Trim(), unit.Value);
            if (LooksLikeUnitWord(argument[(lastColon + 1)..]))
                return null;
        }
        return new SpanOutput(argument.Trim(), SpanUnit.Human);
    }

    private static bool LooksLikeUnitWord(string text) =>
        text.Trim().Length > 0 && text.Trim().All(char.IsLetter);

    private static SpanUnit? ParseSpanUnit(string text) =>
        text.Trim().ToLowerInvariant() switch
        {
            "years" or "year" => SpanUnit.Years,
            "months" or "month" => SpanUnit.Months,
            "weeks" or "week" => SpanUnit.Weeks,
            "days" or "day" => SpanUnit.Days,
            "hours" or "hour" => SpanUnit.Hours,
            "minutes" or "minute" => SpanUnit.Minutes,
            "seconds" or "second" => SpanUnit.Seconds,
            "breakdown" => SpanUnit.Breakdown,
            "human" => SpanUnit.Human,
            _ => null,
        };

    private static Result<DateExpression> Fail(string message) =>
        Result<DateExpression>.Failure(message, ErrorCode);

    private static Result<T> Fail<T>(string message) => Result<T>.Failure(message, ErrorCode);

    [GeneratedRegex(@"^([+-]\d{1,6}(y|mo|w|d|h|m|s))+$", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetPattern();

    [GeneratedRegex(@"([+-]\d{1,6})(mo|y|w|d|h|m|s)", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetPartPattern();
}
