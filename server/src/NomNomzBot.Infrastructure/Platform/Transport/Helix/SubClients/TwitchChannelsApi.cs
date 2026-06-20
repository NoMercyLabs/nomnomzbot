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
/// The Helix "Channels" sub-client (twitch-helix.md §3.2). Pure Helix I/O: it resolves the tenant
/// <see cref="Guid"/> to a Twitch channel id, pre-checks the required scope, builds a
/// <see cref="TwitchHelixRequest"/>, and maps the response through <see cref="ITwitchHelixTransport"/>.
/// It deliberately holds no database or event-bus dependency — mirroring Twitch state into local tables
/// and raising domain events is a separate responsibility owned by the consuming services, which keeps
/// every sub-client thin, uniform, and testable purely at the HTTP seam.
/// </summary>
public sealed class TwitchChannelsApi(
    ITwitchHelixTransport transport,
    ITwitchIdentityResolver identity,
    ITwitchTokenResolver tokens
) : ITwitchChannelsApi
{
    public async Task<Result<TwitchChannelInformation>> GetChannelInformationAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchChannelInformation>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "channels",
            TwitchHelixAuth.App,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value)]
        );

        return await transport.GetSingleAsync<TwitchChannelInformation>(request, ct);
    }

    public async Task<Result> ModifyChannelInformationAsync(
        Guid broadcasterId,
        ModifyChannelInformationRequest request,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelManageBroadcast,
            ct
        );
        if (scope.IsFailure)
            return scope;

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel;

        TwitchHelixRequest helixRequest = new(
            HttpMethod.Patch,
            "channels",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value)],
            Body: request,
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendAsync(helixRequest, ct);
    }

    public async Task<Result<IReadOnlyList<TwitchChannelEditor>>> GetChannelEditorsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(broadcasterId, TwitchScopes.ChannelReadEditors, ct);
        if (scope.IsFailure)
            return scope.WithValue<IReadOnlyList<TwitchChannelEditor>>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<IReadOnlyList<TwitchChannelEditor>>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "channels/editors",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value)]
        );

        return await transport.GetListAsync<TwitchChannelEditor>(request, ct);
    }

    public async Task<Result<TwitchPage<TwitchFollowedChannel>>> GetFollowedChannelsAsync(
        Guid broadcasterId,
        string? filterTwitchBroadcasterId,
        TwitchPageRequest page,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(broadcasterId, TwitchScopes.UserReadFollows, ct);
        if (scope.IsFailure)
            return scope.WithValue<TwitchPage<TwitchFollowedChannel>>(default!);

        Result<string> user = await ResolveAsync(broadcasterId, ct);
        if (user.IsFailure)
            return user.WithValue<TwitchPage<TwitchFollowedChannel>>(default!);

        List<KeyValuePair<string, string>> query =
        [
            new("user_id", user.Value),
            new("first", page.PageSize.ToString()),
        ];
        if (filterTwitchBroadcasterId is not null)
            query.Add(new("broadcaster_id", filterTwitchBroadcasterId));
        if (page.After is not null)
            query.Add(new("after", page.After));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "channels/followed",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query
        );

        return await transport.GetPageAsync<TwitchFollowedChannel>(request, ct);
    }

    public async Task<Result<TwitchPage<TwitchChannelFollower>>> GetChannelFollowersAsync(
        Guid broadcasterId,
        TwitchPageRequest page,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ModeratorReadFollowers,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchPage<TwitchChannelFollower>>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchPage<TwitchChannelFollower>>(default!);

        List<KeyValuePair<string, string>> query =
        [
            new("broadcaster_id", channel.Value),
            new("first", page.PageSize.ToString()),
        ];
        if (page.After is not null)
            query.Add(new("after", page.After));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "channels/followers",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query
        );

        return await transport.GetPageAsync<TwitchChannelFollower>(request, ct);
    }

    public async Task<Result<int>> GetChannelFollowerCountAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ModeratorReadFollowers,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue(0);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue(0);

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "channels/followers",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value), new("first", "1")]
        );

        return await transport.GetTotalAsync(request, ct);
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
