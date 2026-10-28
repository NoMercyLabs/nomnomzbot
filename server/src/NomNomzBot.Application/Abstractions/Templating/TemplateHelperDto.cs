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

namespace NomNomzBot.Application.Abstractions.Templating;

/// <summary>
/// Wire shape for one entry of <c>GET /api/v1/templates/helpers?context=</c> (S042). <see cref="Key"/> is
/// the placeholder text as written in a template; <see cref="DescriptionKey"/> is the i18n key the
/// dashboard resolves for display — never resolved English text (the backend never ships English
/// literals for user-facing strings).
/// </summary>
public sealed record TemplateHelperDto(string Key, string DescriptionKey, string Sample = "")
{
    public static TemplateHelperDto FromEntry(TemplateHelperEntry entry) =>
        new(entry.Key, entry.Description.Key, entry.Sample);
}
