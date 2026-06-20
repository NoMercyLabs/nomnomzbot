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
/// The Helix "Bits" sub-client (twitch-helix.md §3.2). Pure Helix I/O: it resolves the tenant
/// <see cref="Guid"/> to a Twitch channel id, pre-checks the required scope, builds a
/// <see cref="TwitchHelixRequest"/>, and maps the response through <see cref="ITwitchHelixTransport"/>.
/// It deliberately holds no database or event-bus dependency — mirroring Twitch state into local tables
/// and raising domain events is a separate responsibility owned by the consuming services, which keeps
/// every sub-client thin, uniform, and testable purely at the HTTP seam.
/// </summary>
public sealed class TwitchBitsApi(
    ITwitchHelixTransport transport,
    ITwitchIdentityResolver identity,
    ITwitchTokenResolver tokens
) : ITwitchBitsApi
{
    public async Task<Result<IReadOnlyList<TwitchBitsLeaderboardEntry>>> GetBitsLeaderboardAsync(
        Guid broadcasterId,
        int? count,
        string? period,
        DateTimeOffset? startedAt,
        string? userId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(broadcasterId, TwitchScopes.BitsRead, ct);
        if (scope.IsFailure)
            return scope.WithValue<IReadOnlyList<TwitchBitsLeaderboardEntry>>(default!);

        // The leaderboard reads the authenticated broadcaster from the user token itself, so there is no
        // broadcaster_id query param — but the tenant still has to be known locally to select that token.
        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<IReadOnlyList<TwitchBitsLeaderboardEntry>>(default!);

        List<KeyValuePair<string, string>> query = [];
        if (count is not null)
            query.Add(new("count", count.Value.ToString()));
        if (period is not null)
            query.Add(new("period", period));
        if (startedAt is not null)
            query.Add(new("started_at", startedAt.Value.ToString("o")));
        if (userId is not null)
            query.Add(new("user_id", userId));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "bits/leaderboard",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query
        );

        return await transport.GetListAsync<TwitchBitsLeaderboardEntry>(request, ct);
    }

    public async Task<Result<IReadOnlyList<TwitchCheermote>>> GetCheermotesAsync(
        Guid? broadcasterId,
        CancellationToken ct = default
    )
    {
        List<KeyValuePair<string, string>> query = [];
        if (broadcasterId is not null)
        {
            Result<string> channel = await ResolveAsync(broadcasterId.Value, ct);
            if (channel.IsFailure)
                return channel.WithValue<IReadOnlyList<TwitchCheermote>>(default!);
            query.Add(new("broadcaster_id", channel.Value));
        }

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "bits/cheermotes",
            TwitchHelixAuth.App,
            Query: query
        );

        return await transport.GetListAsync<TwitchCheermote>(request, ct);
    }

    public async Task<Result<IReadOnlyList<TwitchCustomPowerUp>>> GetCustomPowerUpsAsync(
        Guid broadcasterId,
        IReadOnlyList<string>? ids,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(broadcasterId, TwitchScopes.BitsRead, ct);
        if (scope.IsFailure)
            return scope.WithValue<IReadOnlyList<TwitchCustomPowerUp>>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<IReadOnlyList<TwitchCustomPowerUp>>(default!);

        List<KeyValuePair<string, string>> query = [new("broadcaster_id", channel.Value)];
        if (ids is not null)
            foreach (string id in ids)
                query.Add(new("id", id));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "bits/custom_power_ups",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query
        );

        return await transport.GetListAsync<TwitchCustomPowerUp>(request, ct);
    }

    /// <summary>Resolves the tenant Guid to its Twitch channel id, or <c>not_found</c> when unknown locally.</summary>
    private async Task<Result<string>> ResolveAsync(Guid broadcasterId, CancellationToken ct)
    {
        string? channelId = await identity.GetTwitchChannelIdAsync(broadcasterId, ct);
        return channelId is null
            ? Result.Failure<string>("Channel is not known locally.", TwitchErrorCodes.NotFound)
            : Result.Success(channelId);
    }

    /// <summary>Pre-checks a required user-token scope, short-circuiting with <c>missing_scope</c> when absent.</summary>
    private async Task<Result> RequireScopeAsync(
        Guid broadcasterId,
        string scope,
        CancellationToken ct
    )
    {
        bool granted = await tokens.HasScopeAsync(broadcasterId, scope, ct);
        return granted
            ? Result.Success()
            : Result.Failure($"Missing required scope '{scope}'.", TwitchErrorCodes.MissingScope);
    }
}
