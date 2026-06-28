// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Infrastructure.Identity.Providers;

/// <summary>
/// <see cref="IPronounProvider"/> backed by the alejo.io API. The catalog is fetched on demand
/// and returned as a key-indexed dictionary for O(1) resolution. Per-viewer lookup delegates to
/// <see cref="IAlejoPronounClient.LookupUserAsync"/>.
/// </summary>
public sealed class AlejoPronounProvider : IPronounProvider
{
    private readonly IAlejoPronounClient _client;

    public AlejoPronounProvider(IAlejoPronounClient client)
    {
        _client = client;
    }

    public string Name => "alejo";

    public async Task<IReadOnlyDictionary<string, PronounCatalogEntry>?> GetCatalogAsync(
        CancellationToken ct = default
    )
    {
        IReadOnlyList<PronounRecord>? records = await _client.FetchAsync(ct);
        if (records is null)
            return null;

        Dictionary<string, PronounCatalogEntry> catalog = new(
            records.Count,
            StringComparer.OrdinalIgnoreCase
        );
        foreach (PronounRecord record in records)
        {
            string subject = record.Subject.ToLowerInvariant();
            string obj = record.Object.ToLowerInvariant();
            string name = subject == obj ? subject : $"{subject}/{obj}";
            catalog[record.Key] = new PronounCatalogEntry(
                record.Key,
                subject,
                obj,
                record.Singular,
                name
            );
        }

        return catalog;
    }

    public async Task<ResolvedPronounRef?> LookupAsync(
        string twitchLogin,
        CancellationToken ct = default
    )
    {
        AlejoUserPronoun? user = await _client.LookupUserAsync(twitchLogin, ct);
        if (user is null)
            return null;

        return new ResolvedPronounRef(user.PronounId, user.AltPronounId);
    }
}
