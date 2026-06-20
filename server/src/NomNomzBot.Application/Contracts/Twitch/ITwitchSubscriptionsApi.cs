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

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// The Helix "Subscriptions" category sub-client: the channel's subscriber list, a single user's subscription
/// status, and the subscriber count (twitch-helix.md §3.2). One of the grouped sub-clients exposed by
/// <see cref="ITwitchHelixClient"/>. Every method takes the owning tenant as a <see cref="Guid"/> and resolves
/// it to the Twitch id internally (the invariant: a Guid never reaches Twitch). Each returns
/// <see cref="Result"/>/<see cref="Result{T}"/> carrying a closed <see cref="TwitchErrorCodes"/> on failure.
/// </summary>
public interface ITwitchSubscriptionsApi
{
    /// <summary>
    /// Get Broadcaster Subscriptions — one page of the channel's subscribers, optionally filtered to specific
    /// target users (raw Twitch ids). Requires <c>channel:read:subscriptions</c>.
    /// </summary>
    Task<Result<TwitchPage<TwitchBroadcasterSubscription>>> GetBroadcasterSubscriptionsAsync(
        Guid broadcasterId,
        IReadOnlyList<string>? filterTwitchUserIds,
        TwitchPageRequest page,
        CancellationToken ct = default
    );

    /// <summary>
    /// Check User Subscription — whether a target user (raw Twitch id) subscribes to the channel. An empty
    /// response (<c>not_found</c>) means "not subscribed". Requires <c>user:read:subscriptions</c>.
    /// </summary>
    Task<Result<TwitchUserSubscription>> CheckUserSubscriptionAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        CancellationToken ct = default
    );

    /// <summary>Get the total subscriber count (<c>?first=1</c>, reads <c>total</c>). Requires <c>channel:read:subscriptions</c>.</summary>
    Task<Result<int>> GetSubscriberCountAsync(Guid broadcasterId, CancellationToken ct = default);
}
