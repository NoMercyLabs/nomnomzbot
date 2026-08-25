// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Localization;

/// <summary>
/// A backend-authored, user-facing string carried in both supported locales (S-SCHEMA-I18N). Backend schema
/// authors (widget settings fields, pipeline action field descriptors) construct one of these per label/help
/// string instead of a bare English literal, so the dashboard can render the viewer's locale without a second,
/// hand-maintained translation pipeline on the backend. <see cref="Key"/> identifies the string for tooling/tests
/// (not shown to the user); <see cref="En"/> and <see cref="Nl"/> are the resolved values for the two supported
/// languages (en/nl per CLAUDE.md i18n). Both are required — an author who forgets a translation gets a compile
/// error, not a silently-English UI; a guard test additionally rejects a blank value that only satisfies the
/// compiler (e.g. an empty-string placeholder).
/// </summary>
public sealed record LocalizedText(string Key, string En, string Nl)
{
    /// <summary>Resolves to <see cref="Nl"/> for a Dutch locale tag (<c>nl</c>, <c>nl-NL</c>, …), else <see cref="En"/>.</summary>
    public string Resolve(string locale) =>
        locale.StartsWith("nl", StringComparison.OrdinalIgnoreCase) ? Nl : En;
}
