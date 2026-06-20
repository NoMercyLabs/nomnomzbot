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
/// The Helix "Channel Points" sub-client (twitch-helix.md §3.2). Pure Helix I/O: it resolves the tenant
/// <see cref="Guid"/> to a Twitch channel id, pre-checks the required scope, builds a
/// <see cref="TwitchHelixRequest"/>, and maps the response through <see cref="ITwitchHelixTransport"/>.
/// It deliberately holds no database or event-bus dependency — mirroring Twitch state into local tables
/// and raising domain events is a separate responsibility owned by the consuming services, which keeps
/// every sub-client thin, uniform, and testable purely at the HTTP seam.
/// </summary>
public sealed class TwitchChannelPointsApi(
    ITwitchHelixTransport transport,
    ITwitchIdentityResolver identity,
    ITwitchTokenResolver tokens
) : ITwitchChannelPointsApi
{
    public async Task<Result<TwitchCustomReward>> CreateCustomRewardAsync(
        Guid broadcasterId,
        CreateCustomRewardRequest request,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelManageRedemptions,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchCustomReward>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchCustomReward>(default!);

        TwitchHelixRequest helixRequest = new(
            HttpMethod.Post,
            "channel_points/custom_rewards",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value)],
            Body: request,
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchCustomReward>(helixRequest, ct);
    }

    public async Task<Result<TwitchCustomReward>> UpdateCustomRewardAsync(
        Guid broadcasterId,
        string rewardId,
        UpdateCustomRewardRequest request,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelManageRedemptions,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchCustomReward>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchCustomReward>(default!);

        TwitchHelixRequest helixRequest = new(
            HttpMethod.Patch,
            "channel_points/custom_rewards",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value), new("id", rewardId)],
            Body: request,
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchCustomReward>(helixRequest, ct);
    }

    public async Task<Result> DeleteCustomRewardAsync(
        Guid broadcasterId,
        string rewardId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelManageRedemptions,
            ct
        );
        if (scope.IsFailure)
            return scope;

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel;

        TwitchHelixRequest helixRequest = new(
            HttpMethod.Delete,
            "channel_points/custom_rewards",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value), new("id", rewardId)],
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendAsync(helixRequest, ct);
    }

    public async Task<Result<IReadOnlyList<TwitchCustomReward>>> GetCustomRewardsAsync(
        Guid broadcasterId,
        IReadOnlyList<string>? rewardIds = null,
        bool onlyManageableRewards = false,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelReadRedemptions,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<IReadOnlyList<TwitchCustomReward>>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<IReadOnlyList<TwitchCustomReward>>(default!);

        List<KeyValuePair<string, string>> query = [new("broadcaster_id", channel.Value)];
        if (rewardIds is not null)
            foreach (string rewardId in rewardIds)
                query.Add(new("id", rewardId));
        if (onlyManageableRewards)
            query.Add(new("only_manageable_rewards", "true"));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "channel_points/custom_rewards",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query
        );

        return await transport.GetListAsync<TwitchCustomReward>(request, ct);
    }

    public async Task<
        Result<TwitchPage<TwitchCustomRewardRedemption>>
    > GetCustomRewardRedemptionsAsync(
        Guid broadcasterId,
        string rewardId,
        string? status,
        IReadOnlyList<string>? redemptionIds,
        string? sort,
        TwitchPageRequest page,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelReadRedemptions,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchPage<TwitchCustomRewardRedemption>>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchPage<TwitchCustomRewardRedemption>>(default!);

        List<KeyValuePair<string, string>> query =
        [
            new("broadcaster_id", channel.Value),
            new("reward_id", rewardId),
            new("first", page.PageSize.ToString()),
        ];
        if (status is not null)
            query.Add(new("status", status));
        if (redemptionIds is not null)
            foreach (string redemptionId in redemptionIds)
                query.Add(new("id", redemptionId));
        if (sort is not null)
            query.Add(new("sort", sort));
        if (page.After is not null)
            query.Add(new("after", page.After));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "channel_points/custom_rewards/redemptions",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query
        );

        return await transport.GetPageAsync<TwitchCustomRewardRedemption>(request, ct);
    }

    public async Task<
        Result<IReadOnlyList<TwitchCustomRewardRedemption>>
    > UpdateRedemptionStatusAsync(
        Guid broadcasterId,
        string rewardId,
        IReadOnlyList<string> redemptionIds,
        UpdateRedemptionStatusRequest request,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelManageRedemptions,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<IReadOnlyList<TwitchCustomRewardRedemption>>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<IReadOnlyList<TwitchCustomRewardRedemption>>(default!);

        List<KeyValuePair<string, string>> query =
        [
            new("broadcaster_id", channel.Value),
            new("reward_id", rewardId),
        ];
        foreach (string redemptionId in redemptionIds)
            query.Add(new("id", redemptionId));

        TwitchHelixRequest helixRequest = new(
            HttpMethod.Patch,
            "channel_points/custom_rewards/redemptions",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query,
            Body: request,
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.GetListAsync<TwitchCustomRewardRedemption>(helixRequest, ct);
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
