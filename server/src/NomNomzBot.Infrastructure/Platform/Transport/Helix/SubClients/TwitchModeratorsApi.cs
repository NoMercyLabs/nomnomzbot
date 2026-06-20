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
/// The Helix "Moderators &amp; VIPs" sub-client (twitch-helix.md §3.2). Pure Helix I/O: it resolves the
/// tenant <see cref="Guid"/> to a Twitch id, pre-checks the required scope, builds a
/// <see cref="TwitchHelixRequest"/>, and maps the response through <see cref="ITwitchHelixTransport"/>.
/// It deliberately holds no database or event-bus dependency — mirroring Twitch state into local tables
/// and raising domain events is a separate responsibility owned by the consuming services, which keeps
/// every sub-client thin, uniform, and testable purely at the HTTP seam.
/// </summary>
public sealed class TwitchModeratorsApi(
    ITwitchHelixTransport transport,
    ITwitchIdentityResolver identity,
    ITwitchTokenResolver tokens
) : ITwitchModeratorsApi
{
    public async Task<Result<TwitchPage<TwitchModerator>>> GetModeratorsAsync(
        Guid broadcasterId,
        TwitchPageRequest page,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(broadcasterId, TwitchScopes.ModerationRead, ct);
        if (scope.IsFailure)
            return scope.WithValue<TwitchPage<TwitchModerator>>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchPage<TwitchModerator>>(default!);

        List<KeyValuePair<string, string>> query =
        [
            new("broadcaster_id", channel.Value),
            new("first", page.PageSize.ToString()),
        ];
        if (page.After is not null)
            query.Add(new("after", page.After));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "moderation/moderators",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query
        );

        return await transport.GetPageAsync<TwitchModerator>(request, ct);
    }

    public async Task<Result> AddModeratorAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelManageModerators,
            ct
        );
        if (scope.IsFailure)
            return scope;

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel;

        TwitchHelixRequest request = new(
            HttpMethod.Post,
            "moderation/moderators",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value), new("user_id", targetTwitchUserId)],
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendAsync(request, ct);
    }

    public async Task<Result> RemoveModeratorAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelManageModerators,
            ct
        );
        if (scope.IsFailure)
            return scope;

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel;

        TwitchHelixRequest request = new(
            HttpMethod.Delete,
            "moderation/moderators",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value), new("user_id", targetTwitchUserId)],
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendAsync(request, ct);
    }

    public async Task<Result<TwitchPage<TwitchVip>>> GetVipsAsync(
        Guid broadcasterId,
        TwitchPageRequest page,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(broadcasterId, TwitchScopes.ChannelReadVips, ct);
        if (scope.IsFailure)
            return scope.WithValue<TwitchPage<TwitchVip>>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchPage<TwitchVip>>(default!);

        List<KeyValuePair<string, string>> query =
        [
            new("broadcaster_id", channel.Value),
            new("first", page.PageSize.ToString()),
        ];
        if (page.After is not null)
            query.Add(new("after", page.After));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "channels/vips",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query
        );

        return await transport.GetPageAsync<TwitchVip>(request, ct);
    }

    public async Task<Result> AddVipAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(broadcasterId, TwitchScopes.ChannelManageVips, ct);
        if (scope.IsFailure)
            return scope;

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel;

        TwitchHelixRequest request = new(
            HttpMethod.Post,
            "channels/vips",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value), new("user_id", targetTwitchUserId)],
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendAsync(request, ct);
    }

    public async Task<Result> RemoveVipAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(broadcasterId, TwitchScopes.ChannelManageVips, ct);
        if (scope.IsFailure)
            return scope;

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel;

        TwitchHelixRequest request = new(
            HttpMethod.Delete,
            "channels/vips",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value), new("user_id", targetTwitchUserId)],
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendAsync(request, ct);
    }

    public async Task<Result<TwitchPage<TwitchModeratedChannel>>> GetModeratedChannelsAsync(
        Guid userId,
        TwitchPageRequest page,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(userId, TwitchScopes.UserReadModeratedChannels, ct);
        if (scope.IsFailure)
            return scope.WithValue<TwitchPage<TwitchModeratedChannel>>(default!);

        Result<string> user = await ResolveUserAsync(userId, ct);
        if (user.IsFailure)
            return user.WithValue<TwitchPage<TwitchModeratedChannel>>(default!);

        List<KeyValuePair<string, string>> query =
        [
            new("user_id", user.Value),
            new("first", page.PageSize.ToString()),
        ];
        if (page.After is not null)
            query.Add(new("after", page.After));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "moderation/channels",
            TwitchHelixAuth.User,
            userId,
            Query: query
        );

        return await transport.GetPageAsync<TwitchModeratedChannel>(request, ct);
    }

    /// <summary>Resolves the tenant Guid to its Twitch channel id, or <c>not_found</c> when unknown locally.</summary>
    private async Task<Result<string>> ResolveAsync(Guid broadcasterId, CancellationToken ct)
    {
        string? channelId = await identity.GetTwitchChannelIdAsync(broadcasterId, ct);
        return channelId is null
            ? Result.Failure<string>("Channel is not known locally.", TwitchErrorCodes.NotFound)
            : Result.Success(channelId);
    }

    /// <summary>Resolves the user Guid to its Twitch user id, or <c>not_found</c> when unknown locally.</summary>
    private async Task<Result<string>> ResolveUserAsync(Guid userId, CancellationToken ct)
    {
        string? twitchUserId = await identity.GetTwitchUserIdAsync(userId, ct);
        return twitchUserId is null
            ? Result.Failure<string>("User is not known locally.", TwitchErrorCodes.NotFound)
            : Result.Success(twitchUserId);
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
