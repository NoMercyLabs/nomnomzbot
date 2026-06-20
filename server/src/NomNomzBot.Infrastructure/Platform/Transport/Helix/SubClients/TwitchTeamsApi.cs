// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Transport.Helix.SubClients;

/// <summary>
/// The Helix "Teams" sub-client (twitch-helix.md §3). Both endpoints are App-token, no-scope reads.
/// <see cref="GetChannelTeamsAsync"/> resolves the tenant <see cref="Guid"/> to a Twitch channel id before
/// building its <see cref="TwitchHelixRequest"/>; <see cref="GetTeamsAsync"/> is keyed on a team name/id — not
/// a tenant — so it uses neither identity resolution nor a scope pre-check. Like every sub-client it holds no
/// database or event-bus dependency: it builds the request and maps the response through
/// <see cref="ITwitchHelixTransport"/>, staying thin and testable purely at the HTTP seam.
/// </summary>
public sealed class TwitchTeamsApi(
    ITwitchHelixTransport transport,
    ITwitchIdentityResolver identity
) : ITwitchTeamsApi
{
    public async Task<Result<IReadOnlyList<TwitchChannelTeam>>> GetChannelTeamsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<IReadOnlyList<TwitchChannelTeam>>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "teams/channel",
            TwitchHelixAuth.App,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value)]
        );

        return await transport.GetListAsync<TwitchChannelTeam>(request, ct);
    }

    public async Task<Result<TwitchTeam>> GetTeamsAsync(
        string? name,
        string? teamId,
        CancellationToken ct = default
    )
    {
        List<KeyValuePair<string, string>> query = [];
        if (name is not null)
            query.Add(new("name", name));
        if (teamId is not null)
            query.Add(new("id", teamId));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "teams",
            TwitchHelixAuth.App,
            Query: query
        );

        return await transport.GetSingleAsync<TwitchTeam>(request, ct);
    }

    /// <summary>Resolves the tenant Guid to its Twitch channel id, or <c>not_found</c> when unknown locally.</summary>
    private async Task<Result<string>> ResolveAsync(Guid broadcasterId, CancellationToken ct)
    {
        string? channelId = await identity.GetTwitchChannelIdAsync(broadcasterId, ct);
        return channelId is null
            ? Result.Failure<string>("Channel is not known locally.", TwitchErrorCodes.NotFound)
            : Result.Success(channelId);
    }
}
