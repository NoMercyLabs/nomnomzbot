// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
            TemplateHelperContexts.Standard,
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
        ParsedArgument.TryWhole(text, out position) && position >= 1;
}
