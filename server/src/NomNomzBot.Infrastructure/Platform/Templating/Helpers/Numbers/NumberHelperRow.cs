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
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

/// <summary>
/// One <c>num.*</c>, <c>math.*</c> or <c>meter.*</c> helper: its canonical key, a sample output and the pure
/// function that renders it from the trimmed arguments and the channel culture (null for a bad argument, so the
/// token prints as written).
/// </summary>
internal sealed record NumberHelperRow(
    string Key,
    string Sample,
    Func<IReadOnlyList<string>, CultureInfo, string?> Render
)
{
    /// <summary>A row that reads its raw arguments and ignores the channel language.</summary>
    public static NumberHelperRow Of(
        string key,
        string sample,
        Func<IReadOnlyList<string>, string?> render
    ) => new(key, sample, (args, _) => render(args));

    /// <summary>A row whose output depends on the channel language.</summary>
    public static NumberHelperRow WithCulture(
        string key,
        string sample,
        Func<IReadOnlyList<string>, CultureInfo, string?> render
    ) => new(key, sample, render);

    /// <summary>A row of exactly one number.</summary>
    public static NumberHelperRow OfNumber(
        string key,
        string sample,
        Func<decimal, string?> render
    ) => OfNumbers(key, sample, 1, 1, values => render(values[0]));

    /// <summary>A row whose arguments are all numbers, between <paramref name="min"/> and <paramref name="max"/> of them.</summary>
    public static NumberHelperRow OfNumbers(
        string key,
        string sample,
        int min,
        int max,
        Func<IReadOnlyList<decimal>, string?> render
    ) =>
        Of(
            key,
            sample,
            args =>
                args.Count >= min
                && args.Count <= max
                && NumberArguments.TryNumbers(args, out List<decimal> values)
                    ? render(values)
                    : null
        );

    public TemplateHelperEntry ToEntry() =>
        new(
            Key,
            [.. Enum.GetValues<TemplateHelperContext>()],
            new LocalizedText("template.helper." + Key)
        )
        {
            Sample = Sample,
            Freshness = TemplateHelperFreshness.Pure,
            Scope = TemplateHelperScope.Global,
        };
}
