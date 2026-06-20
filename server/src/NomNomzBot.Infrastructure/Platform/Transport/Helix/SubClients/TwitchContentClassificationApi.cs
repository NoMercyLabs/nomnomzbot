// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Transport.Helix.SubClients;

/// <summary>
/// The Helix "Content Classification Labels" sub-client (twitch-helix.md §3). A public, no-tenant sub-client:
/// the single endpoint is an App-token, no-scope read keyed only on an optional locale, so there is no scope
/// pre-check and no identity resolution (a locale is not a tenant). Its only dependency is the
/// <see cref="ITwitchHelixTransport"/> — it builds a <see cref="TwitchHelixRequest"/> and maps the list
/// response, staying thin and testable purely at the HTTP seam like every other sub-client.
/// </summary>
public sealed class TwitchContentClassificationApi(ITwitchHelixTransport transport)
    : ITwitchContentClassificationApi
{
    public async Task<
        Result<IReadOnlyList<TwitchContentClassificationLabel>>
    > GetContentClassificationLabelsAsync(string? locale, CancellationToken ct = default)
    {
        List<KeyValuePair<string, string>> queryParams = [];
        if (locale is not null)
            queryParams.Add(new("locale", locale));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "content_classification_labels",
            TwitchHelixAuth.App,
            Query: queryParams
        );

        return await transport.GetListAsync<TwitchContentClassificationLabel>(request, ct);
    }
}
