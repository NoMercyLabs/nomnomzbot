// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Overlays;

/// <summary>
/// Decides which journaled events belong on the generic overlay feed. Overlays render user-facing facts — chat,
/// alerts, redemptions, now-playing, custom events — NOT the platform's internal plumbing (EventSub lifecycle,
/// token refresh, deployment, projections). This keeps that noise off the wire. It is a pragmatic denylist: the
/// default is "public", so a new user-facing event flows automatically; the authoritative public/internal split
/// moves to the event catalog (automation-api.md) when that lands.
/// </summary>
public static class OverlayEventFilter
{
    // Internal event families an overlay could never render. Prefix match on the PascalCase domain-event type name.
    private static readonly string[] InternalPrefixes =
    [
        "EventSub",
        "TwitchHelix",
        "Integration",
        "Deployment",
        "Projection",
        "Authorization",
    ];

    // Events that reach overlays in a RICHER, render-ready form via a dedicated broadcaster, so the raw
    // journaled event must NOT also ride the generic feed (a widget would otherwise get a useless duplicate
    // with none of the decoration). ChatMessageReceivedEvent → ChatMessageBroadcastHandler re-emits it as a
    // decorated "ChatMessage" overlay event (resolved emotes/badges/fragments/colour/avatar/pronouns). The
    // user-facing alerts below → their dashboard broadcast handler re-emits them via OverlayAlertBroadcast as a
    // decorated overlay event (avatar/pronouns/community standing + the event's resolved fields), the SAME dto the
    // dashboard receives; the raw journaled form (the PascalCase event class name) is dropped here so the generic
    // feed carries only the decorated one.
    private static readonly HashSet<string> DecoratedElsewhere = new(StringComparer.Ordinal)
    {
        "ChatMessageReceivedEvent",
        "FollowEvent",
        "NewSubscriptionEvent",
        "ResubscriptionEvent",
        "GiftSubscriptionEvent",
        "CheerEvent",
        "RaidEvent",
        "RewardRedeemedEvent",
        "ModeratorAddedEvent",
        "ModeratorRemovedEvent",
        "VipAddedEvent",
        "VipRemovedEvent",
        "ShoutoutReceivedEvent",
        "UserBannedEvent",
        "UserTimedOutEvent",
        "UserUnbannedEvent",
        "HypeTrainBeganEvent",
        "HypeTrainProgressEvent",
        "HypeTrainEndedEvent",
    };

    /// <summary>
    /// The curated roster of user-facing business events. On the overlay wire these are dropped from the generic
    /// feed only because a dedicated broadcaster re-emits them decorated — for OTHER forwarders (outbound webhooks)
    /// this is exactly the positive allowlist of "things a viewer/integration cares about". The outbound webhook
    /// catalogue reuses this set as its seed rather than maintaining a parallel copy (webhooks.md §9).
    /// </summary>
    public static IReadOnlySet<string> UserFacingBusinessEvents => DecoratedElsewhere;

    /// <summary>
    /// True when the event is internal platform plumbing (EventSub/Helix lifecycle, integration/deployment/projection/
    /// authorization machinery) that no user-facing forwarder should ever carry. Reused by the outbound webhook
    /// catalogue to prove no catalogue entry is internal noise.
    /// </summary>
    public static bool IsInternalPlumbing(string eventType)
    {
        foreach (string prefix in InternalPrefixes)
            if (eventType.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        return false;
    }

    public static bool ShouldForward(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            return false;

        // A decorated re-broadcast owns this event on the overlay wire; drop the raw journaled duplicate.
        if (DecoratedElsewhere.Contains(eventType))
            return false;

        if (IsInternalPlumbing(eventType))
            return false;

        // Raw Twitch EventSub topic names (lowercase, dotted — e.g. "channel.chat.message") are the wire duplicates
        // of the clean PascalCase domain events; forward the domain form only. The "custom." / "supporter." dotted
        // namespaces are genuine user-facing feeds and stay public.
        if (IsRawWireTopic(eventType))
            return false;

        return true;
    }

    private static bool IsRawWireTopic(string eventType) =>
        eventType.Contains('.', StringComparison.Ordinal)
        && !eventType.StartsWith("custom.", StringComparison.OrdinalIgnoreCase)
        && !eventType.StartsWith("supporter.", StringComparison.OrdinalIgnoreCase)
        && string.Equals(eventType, eventType.ToLowerInvariant(), StringComparison.Ordinal);
}
