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
/// The Helix "Games" sub-client (twitch-helix.md §3). A public, no-tenant sub-client: both endpoints are
/// App-token, no-scope reads keyed on identifiers (or nothing at all), so there is no scope pre-check and no
/// identity resolution (a game id is not a tenant). Its only dependency is the
/// <see cref="ITwitchHelixTransport"/> — it builds a <see cref="TwitchHelixRequest"/> and maps the response,
/// staying thin and testable purely at the HTTP seam like every other sub-client.
/// </summary>
public sealed class TwitchGamesApi(ITwitchHelixTransport transport) : ITwitchGamesApi
{
    public async Task<Result<IReadOnlyList<TwitchGame>>> GetGamesAsync(
        IReadOnlyList<string>? ids,
        IReadOnlyList<string>? names,
        IReadOnlyList<string>? igdbIds,
        CancellationToken ct = default
    )
    {
        List<KeyValuePair<string, string>> queryParams = [];
        if (ids is not null)
            foreach (string id in ids)
                queryParams.Add(new("id", id));
        if (names is not null)
            foreach (string name in names)
                queryParams.Add(new("name", name));
        if (igdbIds is not null)
            foreach (string igdbId in igdbIds)
                queryParams.Add(new("igdb_id", igdbId));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "games",
            TwitchHelixAuth.App,
            Query: queryParams
        );

        return await transport.GetListAsync<TwitchGame>(request, ct);
    }

    public async Task<Result<TwitchPage<TwitchGame>>> GetTopGamesAsync(
        TwitchPageRequest page,
        CancellationToken ct = default
    )
    {
        List<KeyValuePair<string, string>> queryParams = [new("first", page.PageSize.ToString())];
        if (page.After is not null)
            queryParams.Add(new("after", page.After));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "games/top",
            TwitchHelixAuth.App,
            Query: queryParams
        );

        return await transport.GetPageAsync<TwitchGame>(request, ct);
    }
}
