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
using Microsoft.Extensions.Logging;
using NodaTime;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Platform.Templating.Dates;

/// <summary>
/// Expands every <c>{date:&lt;source&gt;|&lt;step&gt;|…}</c> token in a rendered template: parse, read the
/// source, calculate, format. A nested placeholder in the source or a <c>diff:</c> date is already plain text
/// when this runs (the resolver substitutes variables first). An invalid token stays exactly as written and
/// logs one warning; nothing here throws into chat.
/// </summary>
public static partial class DateExpressionRenderer
{
    private const int MaxDepth = 5;

    public static string ExpandAll(string text, DateExpressionContext context, ILogger logger)
    {
        if (!text.Contains("{date:", StringComparison.OrdinalIgnoreCase))
            return text;

        HashSet<string> failed = new(StringComparer.Ordinal);
        string current = text;
        for (int depth = 0; depth < MaxDepth; depth++)
        {
            string next = DatePattern()
                .Replace(current, match => ExpandOne(match, context, logger, failed));
            if (next == current)
                return next;
            current = next;
        }
        return current;
    }

    /// <summary>Renders one expression payload (the text after <c>date:</c>).</summary>
    public static Result<string> Render(string payload, DateExpressionContext context)
    {
        Result<DateExpression> parsed = DateExpressionParser.Parse(payload);
        if (parsed.IsFailure)
            return Result<string>.Failure(parsed.ErrorMessage!, parsed.ErrorCode);

        DateExpression expression = parsed.Value;
        ZonedDateTime? start = DateSourceReader.Read(expression.Source, context);
        if (start is null)
            return Result<string>.Failure(
                $"'{expression.Source}' is not a date",
                "TEMPLATE_INVALID_DATE_EXPRESSION"
            );

        CultureInfo culture = expression.Culture ?? context.Culture;
        try
        {
            ZonedDateTime result = DateCalculator.Apply(start.Value, expression.Steps);
            return DateOutputFormatter.Render(result, expression.Output, context, culture);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
        {
            // A calculation past the supported calendar range (year 9999) is a template mistake.
            return Result<string>.Failure(
                "the result is outside the supported date range",
                "TEMPLATE_INVALID_DATE_EXPRESSION"
            );
        }
    }

    private static string ExpandOne(
        Match match,
        DateExpressionContext context,
        ILogger logger,
        HashSet<string> failed
    )
    {
        if (failed.Contains(match.Value))
            return match.Value;

        Result<string> rendered = Render(match.Groups[1].Value, context);
        if (rendered.IsSuccess)
            return rendered.Value;

        failed.Add(match.Value);
        logger.LogWarning(
            "Template date expression '{Expression}' is invalid: {Error}",
            match.Value,
            rendered.ErrorMessage
        );
        return match.Value;
    }

    [GeneratedRegex(@"\{date:([^{}]+)\}", RegexOptions.IgnoreCase)]
    private static partial Regex DatePattern();
}
