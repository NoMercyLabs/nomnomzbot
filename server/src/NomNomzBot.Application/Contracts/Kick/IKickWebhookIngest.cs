// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Kick;

/// <summary>
/// Turns VERIFIED Kick webhook deliveries into their canonical effects, routed by event type so the
/// webhook controller never grows a dispatch chain. <c>chat.message.sent</c> becomes the canonical
/// <c>ChatMessageReceivedEvent</c> (<c>Provider = kick</c>) — the same one substrate Twitch and YouTube
/// chat ride; the community/monetization events (follows, subs, gifts, kicks, reward redemptions) publish
/// the SAME domain events their Twitch EventSub twins do, so alerts, engagement earning, and the hub
/// broadcasts all fire with zero Kick-specific consumers; the livestream events stamp the Kick tenant's
/// <c>Channel</c> row (<c>IsLive</c>/title/category) behind the dashboard's <c>platformsLive</c>. An
/// event type without a consumer is a silent no-op (the delivery is still acknowledged). The caller
/// (webhook controller) owns signature + freshness checks; this owns tenant resolution, redelivery
/// dedupe, and the payload translation.
/// </summary>
public interface IKickWebhookIngest
{
    Task HandleAsync(
        string eventType,
        string rawBody,
        CancellationToken cancellationToken = default
    );
}
