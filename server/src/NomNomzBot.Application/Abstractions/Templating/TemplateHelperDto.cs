// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Templating;

/// <summary>
/// Wire shape for one entry of <c>GET /api/v1/templates/helpers?context=</c> (S042). <see cref="Key"/> is
/// the placeholder text as written in a template; <see cref="DescriptionKey"/> is the i18n key the
/// dashboard resolves for display — never resolved English text (the backend never ships English
/// literals for user-facing strings).
/// </summary>
public sealed record TemplateHelperDto(string Key, string DescriptionKey)
{
    public static TemplateHelperDto FromEntry(TemplateHelperEntry entry) =>
        new(entry.Key, entry.Description.Key);
}
