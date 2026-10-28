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
