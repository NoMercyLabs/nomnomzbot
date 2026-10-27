// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Text;

/// <summary>
/// One <c>text.*</c> helper: its canonical key, a sample output and the pure function that renders it from the
/// trimmed arguments (null for a bad argument, so the token prints as written).
/// </summary>
internal sealed record TextHelperRow(
    string Key,
    string Sample,
    Func<IReadOnlyList<string>, string?> Render
)
{
    /// <summary>A row whose function takes the whole text as its only argument.</summary>
    public static TextHelperRow OfText(string key, string sample, Func<string, string?> render) =>
        new(key, sample, args => args.Count == 1 ? render(args[0]) : null);

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
