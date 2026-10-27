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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Composing;

/// <summary>
/// The one way the composing providers (chatter, quote, variable, include) build a registry row: valid in every
/// context, help string <c>template.helper.&lt;key&gt;</c> unless a key is named.
/// </summary>
internal static class ComposingRows
{
    public static TemplateHelperEntry Row(
        string key,
        string sample,
        TemplateHelperFreshness freshness,
        string? source = null,
        string? prefix = null,
        string? descriptionKey = null
    ) =>
        new(
            key,
            [.. Enum.GetValues<TemplateHelperContext>()],
            new LocalizedText(descriptionKey ?? "template.helper." + key),
            prefix
        )
        {
            Sample = sample,
            Source = source,
            Freshness = freshness,
            Scope = TemplateHelperScope.Channel,
        };

    /// <summary>A whole number of at least 1 (a quote number or a list position).</summary>
    public static bool TryPosition(string text, out int position) =>
        int.TryParse(
            text,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out position
        )
        && position >= 1;
}
