// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Identity.Entities;

/// <summary>
/// Single source of truth for turning a viewer's resolved <see cref="User.Pronoun"/> /
/// <see cref="User.AltPronoun"/> pair into a display string (spec D3). Shared by the
/// <c>{{user.pronouns}}</c> template variable (<c>TemplateResolver</c>) and the dashboard hub
/// enrichment payloads (<c>HubUserEnrichmentStore</c>) so both surfaces render pronouns identically —
/// regardless of whether the pair was set by lazy alejo.io resolution or a manual viewer override.
/// </summary>
public static class UserPronounDisplay
{
    /// <summary>
    /// With an alt pronoun set: the two subjects as a badge (e.g. "she/they" for primary=she/her +
    /// alt=they/them). Without one: the primary pronoun's own name (e.g. "they/them"). Null when the
    /// viewer has no primary pronoun resolved yet.
    /// </summary>
    public static string? Format(Pronoun? primary, Pronoun? alt) =>
        primary is null ? null
        : alt is not null ? $"{primary.Subject}/{alt.Subject}"
        : primary.Name;
}
