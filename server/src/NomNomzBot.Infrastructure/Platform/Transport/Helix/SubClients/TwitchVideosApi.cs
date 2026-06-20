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
/// The Helix "Videos" sub-client (twitch-helix.md §3.2). Pure Helix I/O: it resolves the tenant
/// <see cref="Guid"/> to a Twitch user id, pre-checks the required scope, builds a
/// <see cref="TwitchHelixRequest"/>, and maps the response through <see cref="ITwitchHelixTransport"/>.
/// It deliberately holds no database or event-bus dependency — mirroring Twitch state into local tables
/// and raising domain events is a separate responsibility owned by the consuming services, which keeps
/// every sub-client thin, uniform, and testable purely at the HTTP seam.
/// </summary>
public sealed class TwitchVideosApi(
    ITwitchHelixTransport transport,
    ITwitchIdentityResolver identity,
    ITwitchTokenResolver tokens
) : ITwitchVideosApi
{
    public async Task<Result<TwitchPage<TwitchVideo>>> GetVideosByBroadcasterAsync(
        Guid broadcasterId,
        string? type,
        string? period,
        string? sort,
        TwitchPageRequest page,
        CancellationToken ct = default
    )
    {
        Result<string> user = await ResolveAsync(broadcasterId, ct);
        if (user.IsFailure)
            return user.WithValue<TwitchPage<TwitchVideo>>(default!);

        List<KeyValuePair<string, string>> query =
        [
            new("user_id", user.Value),
            new("first", page.PageSize.ToString()),
        ];
        if (type is not null)
            query.Add(new("type", type));
        if (period is not null)
            query.Add(new("period", period));
        if (sort is not null)
            query.Add(new("sort", sort));
        if (page.After is not null)
            query.Add(new("after", page.After));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "videos",
            TwitchHelixAuth.App,
            Query: query
        );

        return await transport.GetPageAsync<TwitchVideo>(request, ct);
    }

    public async Task<Result<IReadOnlyList<TwitchVideo>>> GetVideosByIdsAsync(
        IReadOnlyList<string> videoIds,
        CancellationToken ct = default
    )
    {
        List<KeyValuePair<string, string>> query = [];
        foreach (string id in videoIds)
            query.Add(new("id", id));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "videos",
            TwitchHelixAuth.App,
            Query: query
        );

        return await transport.GetListAsync<TwitchVideo>(request, ct);
    }

    public async Task<Result<IReadOnlyList<string>>> DeleteVideosAsync(
        Guid broadcasterId,
        IReadOnlyList<string> videoIds,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(broadcasterId, TwitchScopes.ChannelManageVideos, ct);
        if (scope.IsFailure)
            return scope.WithValue<IReadOnlyList<string>>(default!);

        Result<string> user = await ResolveAsync(broadcasterId, ct);
        if (user.IsFailure)
            return user.WithValue<IReadOnlyList<string>>(default!);

        List<KeyValuePair<string, string>> query = [];
        foreach (string id in videoIds)
            query.Add(new("id", id));

        TwitchHelixRequest request = new(
            HttpMethod.Delete,
            "videos",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query,
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.GetListAsync<string>(request, ct);
    }

    /// <summary>Resolves the tenant Guid to its Twitch user id, or <c>not_found</c> when unknown locally.</summary>
    private async Task<Result<string>> ResolveAsync(Guid broadcasterId, CancellationToken ct)
    {
        string? userId = await identity.GetTwitchUserIdAsync(broadcasterId, ct);
        return userId is null
            ? Result.Failure<string>("User is not known locally.", TwitchErrorCodes.NotFound)
            : Result.Success(userId);
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
