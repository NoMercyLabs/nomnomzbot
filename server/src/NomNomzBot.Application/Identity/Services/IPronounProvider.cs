// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// One catalog + per-viewer lookup pair — one implementation per external provider (R.1 = alejo.io).
/// Add new providers by implementing this interface and registering them; no other code changes required.
/// </summary>
public interface IPronounProvider
{
    /// <summary>Provider slug, e.g. "alejo".</summary>
    string Name { get; }

    /// <summary>
    /// Fetch the full pronoun catalog from the provider. Returns a dictionary keyed by the provider's
    /// own identifier (e.g. "theythem") mapping to the canonical grammar row.
    /// Returns null when the provider is unreachable; the caller falls back to the DB seed.
    /// </summary>
    Task<IReadOnlyDictionary<string, PronounCatalogEntry>?> GetCatalogAsync(
        CancellationToken ct = default
    );

    /// <summary>
    /// Resolve which pronouns a specific viewer has set on the provider. Returns null when the viewer
    /// has no pronouns set or the lookup fails (the caller silently skips the update).
    /// </summary>
    Task<ResolvedPronounRef?> LookupAsync(string twitchLogin, CancellationToken ct = default);
}

/// <summary>One grammar row from an external pronoun provider catalog.</summary>
public sealed record PronounCatalogEntry(
    string Key,
    string Subject,
    string Object,
    bool Singular,
    string Name
);

/// <summary>
/// The viewer's primary pronoun key + optional alt key, as returned by the provider.
/// Keys correspond to <see cref="PronounCatalogEntry.Key"/> values (e.g. "theythem", "sheher").
/// </summary>
public sealed record ResolvedPronounRef(string PronounKey, string? AltPronounKey);
