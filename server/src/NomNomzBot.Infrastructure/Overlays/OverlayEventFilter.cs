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

    public static bool ShouldForward(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            return false;

        foreach (string prefix in InternalPrefixes)
            if (eventType.StartsWith(prefix, StringComparison.Ordinal))
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
