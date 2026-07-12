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
/// The Helix "Chat assets" sub-client (twitch-helix.md §3.2): the read-only chat assets — chatters, emotes,
/// emote sets, user emotes, chat badges, and the active shared chat session. Pure Helix I/O: it resolves the
/// tenant <see cref="Guid"/> to a Twitch id, pre-checks the required scope (where one applies), builds a
/// <see cref="TwitchHelixRequest"/>, and maps the response through <see cref="ITwitchHelixTransport"/>.
/// It deliberately holds no database or event-bus dependency — mirroring Twitch state into local tables
/// and raising domain events is a separate responsibility owned by the consuming services, which keeps
/// every sub-client thin, uniform, and testable purely at the HTTP seam.
/// </summary>
public sealed class TwitchChatAssetsApi(
    ITwitchHelixTransport transport,
    ITwitchIdentityResolver identity,
    ITwitchTokenResolver tokens
) : ITwitchChatAssetsApi
{
    public async Task<Result<TwitchPage<TwitchChatter>>> GetChattersAsync(
        Guid broadcasterId,
        TwitchPageRequest page,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ModeratorReadChatters,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchPage<TwitchChatter>>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchPage<TwitchChatter>>(default!);

        List<KeyValuePair<string, string>> query =
        [
            new("broadcaster_id", channel.Value),
            new("moderator_id", channel.Value),
            new("first", page.PageSize.ToString()),
        ];
        if (page.After is not null)
            query.Add(new("after", page.After));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "chat/chatters",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query
        );

        return await transport.GetPageAsync<TwitchChatter>(request, ct);
    }

    public async Task<Result<IReadOnlyList<TwitchChannelEmote>>> GetChannelEmotesAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<IReadOnlyList<TwitchChannelEmote>>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "chat/emotes",
            TwitchHelixAuth.App,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value)]
        );

        return await transport.GetListAsync<TwitchChannelEmote>(request, ct);
    }

    public async Task<Result<IReadOnlyList<TwitchChannelEmote>>> GetChannelEmotesByTwitchIdAsync(
        string twitchBroadcasterId,
        CancellationToken ct = default
    )
    {
        // No Guid → Twitch-id resolution: the id is already the raw Twitch broadcaster id (the channel may not be
        // a local tenant). Same App-token endpoint, same mapping as GetChannelEmotesAsync(Guid).
        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "chat/emotes",
            TwitchHelixAuth.App,
            Query: [new("broadcaster_id", twitchBroadcasterId)]
        );

        return await transport.GetListAsync<TwitchChannelEmote>(request, ct);
    }

    public async Task<Result<IReadOnlyList<TwitchGlobalEmote>>> GetGlobalEmotesAsync(
        CancellationToken ct = default
    )
    {
        TwitchHelixRequest request = new(HttpMethod.Get, "chat/emotes/global", TwitchHelixAuth.App);

        return await transport.GetListAsync<TwitchGlobalEmote>(request, ct);
    }

    public async Task<Result<IReadOnlyList<TwitchEmoteSetEmote>>> GetEmoteSetsAsync(
        IReadOnlyList<string> emoteSetIds,
        CancellationToken ct = default
    )
    {
        List<KeyValuePair<string, string>> query = [];
        foreach (string emoteSetId in emoteSetIds)
            query.Add(new("emote_set_id", emoteSetId));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "chat/emotes/set",
            TwitchHelixAuth.App,
            Query: query
        );

        return await transport.GetListAsync<TwitchEmoteSetEmote>(request, ct);
    }

    public async Task<Result<TwitchPage<TwitchUserEmote>>> GetUserEmotesAsync(
        Guid broadcasterId,
        string? afterCursor,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(broadcasterId, TwitchScopes.UserReadEmotes, ct);
        if (scope.IsFailure)
            return scope.WithValue<TwitchPage<TwitchUserEmote>>(default!);

        Result<string> user = await ResolveAsync(broadcasterId, ct);
        if (user.IsFailure)
            return user.WithValue<TwitchPage<TwitchUserEmote>>(default!);

        List<KeyValuePair<string, string>> query = [new("user_id", user.Value)];
        if (afterCursor is not null)
            query.Add(new("after", afterCursor));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "chat/emotes/user",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query
        );

        return await transport.GetPageAsync<TwitchUserEmote>(request, ct);
    }

    public async Task<Result<TwitchPage<TwitchUserEmote>>> GetUserEmotesAsOperatorAsync(
        Guid operatorUserId,
        string? broadcasterTwitchId,
        string? afterCursor,
        CancellationToken ct = default
    )
    {
        // The operator's OWN Twitch id is the user_id, and the operator's OWN token signs the call
        // (Auth.Operator resolves it from OperatorUserId) — so these are the operator's PERSONAL cross-channel
        // emotes, not the tenant broadcaster's. No local scope pre-check is possible here: HasScopeAsync inspects
        // the tenant broadcaster's token, but this call rides the operator's token — so Twitch is the authority
        // that the operator's grant carries user:read:emotes (a missing scope surfaces as a typed failure the
        // caller degrades to empty), which means there is no privilege escalation.
        string? operatorTwitchId = await identity.GetTwitchUserIdAsync(operatorUserId, ct);
        if (string.IsNullOrEmpty(operatorTwitchId))
            return Result.Failure<TwitchPage<TwitchUserEmote>>(
                "You have no linked Twitch identity to read emotes as.",
                TwitchErrorCodes.NoToken
            );

        List<KeyValuePair<string, string>> query = [new("user_id", operatorTwitchId)];

        // broadcaster_id is the current channel's RAW Twitch id (never resolved from a Guid — the channel may
        // not be a tenant); when present it guarantees that channel's follower emotes reach the operator's set.
        if (!string.IsNullOrEmpty(broadcasterTwitchId))
            query.Add(new("broadcaster_id", broadcasterTwitchId));
        if (afterCursor is not null)
            query.Add(new("after", afterCursor));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "chat/emotes/user",
            TwitchHelixAuth.Operator,
            Query: query,
            OperatorUserId: operatorUserId
        );

        return await transport.GetPageAsync<TwitchUserEmote>(request, ct);
    }

    public async Task<Result<IReadOnlyList<TwitchChatBadgeSet>>> GetChannelChatBadgesAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<IReadOnlyList<TwitchChatBadgeSet>>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "chat/badges",
            TwitchHelixAuth.App,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value)]
        );

        return await transport.GetListAsync<TwitchChatBadgeSet>(request, ct);
    }

    public async Task<Result<IReadOnlyList<TwitchChatBadgeSet>>> GetGlobalChatBadgesAsync(
        CancellationToken ct = default
    )
    {
        TwitchHelixRequest request = new(HttpMethod.Get, "chat/badges/global", TwitchHelixAuth.App);

        return await transport.GetListAsync<TwitchChatBadgeSet>(request, ct);
    }

    public async Task<Result<TwitchSharedChatSession>> GetSharedChatSessionAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchSharedChatSession>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "chat/shared_chat_session",
            TwitchHelixAuth.App,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value)]
        );

        return await transport.GetSingleAsync<TwitchSharedChatSession>(request, ct);
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
