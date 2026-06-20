// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Twitch;

// Helix "Subscriptions" category wire models (GET /subscriptions, /subscriptions/user). These records
// deserialize straight from Twitch's snake_case JSON via the transport's naming policy — no per-property
// annotations. Twitch ids stay strings (they are the subscriber's / gifter's ids); the owning tenant is
// always passed in as a Guid method argument, never here.

/// <summary>
/// One row of Get Broadcaster Subscriptions — a user that subscribes to the channel, including the gifter
/// when the sub is a gift and the plan tier (<c>1000</c>/<c>2000</c>/<c>3000</c>).
/// </summary>
public sealed record TwitchBroadcasterSubscription(
    string BroadcasterId,
    string BroadcasterLogin,
    string BroadcasterName,
    string GifterId,
    string GifterLogin,
    string GifterName,
    bool IsGift,
    string PlanName,
    string Tier,
    string UserId,
    string UserLogin,
    string UserName
);

/// <summary>
/// The single record returned by Check User Subscription — whether a target user subscribes to the channel,
/// the tier, and (when gifted) the gifter. The gifter fields are absent for non-gift subscriptions, so they
/// are nullable. An empty <c>data[]</c> means "not subscribed" and surfaces as <c>not_found</c>.
/// </summary>
public sealed record TwitchUserSubscription(
    string BroadcasterId,
    string BroadcasterLogin,
    string BroadcasterName,
    bool IsGift,
    string Tier,
    string? GifterId = null,
    string? GifterLogin = null,
    string? GifterName = null
);
