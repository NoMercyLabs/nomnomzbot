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
/// The Helix "Search" sub-client (twitch-helix.md §3). The canonical public, no-tenant sub-client: both
/// endpoints are App-token, no-scope reads keyed on a free-text query, so there is no scope pre-check and no
/// identity resolution (a query string is not a tenant). Its only dependency is the
/// <see cref="ITwitchHelixTransport"/> — it builds a <see cref="TwitchHelixRequest"/> and maps the paged
/// response, staying thin and testable purely at the HTTP seam like every other sub-client.
/// </summary>
public sealed class TwitchSearchApi(ITwitchHelixTransport transport) : ITwitchSearchApi
{
    public async Task<Result<TwitchPage<TwitchSearchCategory>>> SearchCategoriesAsync(
        string query,
        TwitchPageRequest page,
        CancellationToken ct = default
    )
    {
        List<KeyValuePair<string, string>> queryParams =
        [
            new("query", query),
            new("first", page.PageSize.ToString()),
        ];
        if (page.After is not null)
            queryParams.Add(new("after", page.After));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "search/categories",
            TwitchHelixAuth.App,
            Query: queryParams
        );

        return await transport.GetPageAsync<TwitchSearchCategory>(request, ct);
    }

    public async Task<Result<TwitchPage<TwitchSearchChannel>>> SearchChannelsAsync(
        string query,
        bool? liveOnly,
        TwitchPageRequest page,
        CancellationToken ct = default
    )
    {
        List<KeyValuePair<string, string>> queryParams =
        [
            new("query", query),
            new("first", page.PageSize.ToString()),
        ];
        if (liveOnly is not null)
            queryParams.Add(new("live_only", liveOnly.Value ? "true" : "false"));
        if (page.After is not null)
            queryParams.Add(new("after", page.After));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "search/channels",
            TwitchHelixAuth.App,
            Query: queryParams
        );

        return await transport.GetPageAsync<TwitchSearchChannel>(request, ct);
    }
}
