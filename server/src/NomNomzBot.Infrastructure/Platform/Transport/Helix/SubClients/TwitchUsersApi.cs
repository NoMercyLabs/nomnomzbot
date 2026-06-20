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
/// The Helix "Users" sub-client (twitch-helix.md §3.2). Pure Helix I/O: it resolves the tenant
/// <see cref="Guid"/> to a Twitch user id, pre-checks the required scope, builds a
/// <see cref="TwitchHelixRequest"/>, and maps the response through <see cref="ITwitchHelixTransport"/>.
/// It deliberately holds no database or event-bus dependency — mirroring Twitch state into local tables
/// and raising domain events is a separate responsibility owned by the consuming services, which keeps
/// every sub-client thin, uniform, and testable purely at the HTTP seam.
/// </summary>
public sealed class TwitchUsersApi(
    ITwitchHelixTransport transport,
    ITwitchIdentityResolver identity,
    ITwitchTokenResolver tokens
) : ITwitchUsersApi
{
    public async Task<Result<IReadOnlyList<TwitchUser>>> GetUsersByIdsAsync(
        IReadOnlyList<string> twitchUserIds,
        CancellationToken ct = default
    )
    {
        List<KeyValuePair<string, string>> query = [];
        foreach (string id in twitchUserIds)
            query.Add(new("id", id));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "users",
            TwitchHelixAuth.App,
            Query: query
        );

        return await transport.GetListAsync<TwitchUser>(request, ct);
    }

    public async Task<Result<IReadOnlyList<TwitchUser>>> GetUsersByLoginsAsync(
        IReadOnlyList<string> logins,
        CancellationToken ct = default
    )
    {
        List<KeyValuePair<string, string>> query = [];
        foreach (string login in logins)
            query.Add(new("login", login));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "users",
            TwitchHelixAuth.App,
            Query: query
        );

        return await transport.GetListAsync<TwitchUser>(request, ct);
    }

    public async Task<Result<TwitchUser>> UpdateDescriptionAsync(
        Guid broadcasterId,
        string description,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(broadcasterId, TwitchScopes.UserEdit, ct);
        if (scope.IsFailure)
            return scope.WithValue<TwitchUser>(default!);

        Result<string> user = await ResolveUserAsync(broadcasterId, ct);
        if (user.IsFailure)
            return user.WithValue<TwitchUser>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Put,
            "users",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("description", description)],
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchUser>(request, ct);
    }

    public async Task<Result<TwitchPage<TwitchBlockedUser>>> GetBlockListAsync(
        Guid broadcasterId,
        TwitchPageRequest page,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.UserReadBlockedUsers,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchPage<TwitchBlockedUser>>(default!);

        Result<string> user = await ResolveUserAsync(broadcasterId, ct);
        if (user.IsFailure)
            return user.WithValue<TwitchPage<TwitchBlockedUser>>(default!);

        List<KeyValuePair<string, string>> query =
        [
            new("broadcaster_id", user.Value),
            new("first", page.PageSize.ToString()),
        ];
        if (page.After is not null)
            query.Add(new("after", page.After));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "users/blocks",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query
        );

        return await transport.GetPageAsync<TwitchBlockedUser>(request, ct);
    }

    public async Task<Result> BlockUserAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        string? sourceContext = null,
        string? reason = null,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.UserManageBlockedUsers,
            ct
        );
        if (scope.IsFailure)
            return scope;

        Result<string> user = await ResolveUserAsync(broadcasterId, ct);
        if (user.IsFailure)
            return user;

        List<KeyValuePair<string, string>> query = [new("target_user_id", targetTwitchUserId)];
        if (sourceContext is not null)
            query.Add(new("source_context", sourceContext));
        if (reason is not null)
            query.Add(new("reason", reason));

        TwitchHelixRequest request = new(
            HttpMethod.Put,
            "users/blocks",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query,
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendAsync(request, ct);
    }

    public async Task<Result> UnblockUserAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.UserManageBlockedUsers,
            ct
        );
        if (scope.IsFailure)
            return scope;

        Result<string> user = await ResolveUserAsync(broadcasterId, ct);
        if (user.IsFailure)
            return user;

        TwitchHelixRequest request = new(
            HttpMethod.Delete,
            "users/blocks",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("target_user_id", targetTwitchUserId)],
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendAsync(request, ct);
    }

    /// <summary>Resolves the tenant Guid to its Twitch user id, or <c>not_found</c> when unknown locally.</summary>
    private async Task<Result<string>> ResolveUserAsync(Guid broadcasterId, CancellationToken ct)
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
