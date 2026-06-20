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
/// The Helix "Streams" sub-client (twitch-helix.md §3.2). Pure Helix I/O: it resolves the tenant
/// <see cref="Guid"/> to a Twitch channel/user id, pre-checks the required scope, builds a
/// <see cref="TwitchHelixRequest"/>, and maps the response through <see cref="ITwitchHelixTransport"/>.
/// It deliberately holds no database or event-bus dependency — mirroring Twitch state into local tables
/// and raising domain events is a separate responsibility owned by the consuming services, which keeps
/// every sub-client thin, uniform, and testable purely at the HTTP seam.
/// </summary>
public sealed class TwitchStreamsApi(
    ITwitchHelixTransport transport,
    ITwitchIdentityResolver identity,
    ITwitchTokenResolver tokens
) : ITwitchStreamsApi
{
    public async Task<Result<TwitchPage<TwitchStream>>> GetStreamsAsync(
        TwitchStreamsFilter filter,
        TwitchPageRequest page,
        CancellationToken ct = default
    )
    {
        List<KeyValuePair<string, string>> query = [new("first", page.PageSize.ToString())];
        AppendAll(query, "user_id", filter.UserIds);
        AppendAll(query, "user_login", filter.UserLogins);
        AppendAll(query, "game_id", filter.GameIds);
        AppendAll(query, "language", filter.Languages);
        if (filter.Type is not null)
            query.Add(new("type", filter.Type));
        if (page.After is not null)
            query.Add(new("after", page.After));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "streams",
            TwitchHelixAuth.App,
            Query: query
        );

        return await transport.GetPageAsync<TwitchStream>(request, ct);
    }

    public async Task<Result<TwitchStream>> GetStreamAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchStream>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "streams",
            TwitchHelixAuth.App,
            broadcasterId,
            Query: [new("user_id", channel.Value)]
        );

        return await transport.GetSingleAsync<TwitchStream>(request, ct);
    }

    public async Task<Result<string>> GetStreamKeyAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelReadStreamKey,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<string>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<string>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "streams/key",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value)]
        );

        Result<TwitchStreamKey> result = await transport.GetSingleAsync<TwitchStreamKey>(
            request,
            ct
        );
        return result.IsSuccess
            ? Result.Success(result.Value.StreamKey)
            : result.WithValue<string>(default!);
    }

    public async Task<Result<TwitchPage<TwitchStream>>> GetFollowedStreamsAsync(
        Guid broadcasterId,
        TwitchPageRequest page,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(broadcasterId, TwitchScopes.UserReadFollows, ct);
        if (scope.IsFailure)
            return scope.WithValue<TwitchPage<TwitchStream>>(default!);

        Result<string> user = await ResolveUserAsync(broadcasterId, ct);
        if (user.IsFailure)
            return user.WithValue<TwitchPage<TwitchStream>>(default!);

        List<KeyValuePair<string, string>> query =
        [
            new("user_id", user.Value),
            new("first", page.PageSize.ToString()),
        ];
        if (page.After is not null)
            query.Add(new("after", page.After));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "streams/followed",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query
        );

        return await transport.GetPageAsync<TwitchStream>(request, ct);
    }

    public async Task<Result<TwitchStreamMarker>> CreateStreamMarkerAsync(
        Guid broadcasterId,
        string? description = null,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelManageBroadcast,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchStreamMarker>(default!);

        Result<string> user = await ResolveUserAsync(broadcasterId, ct);
        if (user.IsFailure)
            return user.WithValue<TwitchStreamMarker>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Post,
            "streams/markers",
            TwitchHelixAuth.User,
            broadcasterId,
            Body: new CreateStreamMarkerRequest(user.Value, description),
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchStreamMarker>(request, ct);
    }

    public async Task<Result<TwitchPage<TwitchStreamMarkerGroup>>> GetStreamMarkersAsync(
        Guid broadcasterId,
        string? videoId,
        TwitchPageRequest page,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(broadcasterId, TwitchScopes.UserReadBroadcast, ct);
        if (scope.IsFailure)
            return scope.WithValue<TwitchPage<TwitchStreamMarkerGroup>>(default!);

        Result<string> user = await ResolveUserAsync(broadcasterId, ct);
        if (user.IsFailure)
            return user.WithValue<TwitchPage<TwitchStreamMarkerGroup>>(default!);

        List<KeyValuePair<string, string>> query = [new("first", page.PageSize.ToString())];
        if (videoId is not null)
            query.Add(new("video_id", videoId));
        else
            query.Add(new("user_id", user.Value));
        if (page.After is not null)
            query.Add(new("after", page.After));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "streams/markers",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query
        );

        return await transport.GetPageAsync<TwitchStreamMarkerGroup>(request, ct);
    }

    /// <summary>Appends each value of an optional list as its own repeated query parameter.</summary>
    private static void AppendAll(
        List<KeyValuePair<string, string>> query,
        string key,
        IReadOnlyList<string>? values
    )
    {
        if (values is null)
            return;
        foreach (string value in values)
            query.Add(new(key, value));
    }

    /// <summary>Resolves the tenant Guid to its Twitch channel id, or <c>not_found</c> when unknown locally.</summary>
    private async Task<Result<string>> ResolveAsync(Guid broadcasterId, CancellationToken ct)
    {
        string? channelId = await identity.GetTwitchChannelIdAsync(broadcasterId, ct);
        return channelId is null
            ? Result.Failure<string>("Channel is not known locally.", TwitchErrorCodes.NotFound)
            : Result.Success(channelId);
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
