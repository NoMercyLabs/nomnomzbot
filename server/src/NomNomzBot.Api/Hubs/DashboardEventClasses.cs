// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Api.Hubs;

/// <summary>
/// The closed set of dashboard push classes a hub connection subscribes to per channel (BUILD item 5).
/// A page that only renders chat need not receive live-ops or music pushes: <c>JoinChannel</c> joins
/// every class (the universal shell wants everything), <c>JoinChannelClasses</c> joins a subset.
/// Core pushes — stream status, config/permission/reward invalidations, alerts — ride the BASE channel
/// group and are always on; they are what keeps any open page coherent regardless of its class set.
/// </summary>
public static class DashboardEventClasses
{
    public const string Chat = "chat";
    public const string Activity = "activity";
    public const string LiveOps = "liveops";
    public const string Music = "music";
    public const string Moderation = "moderation";

    public static readonly IReadOnlyList<string> All = [Chat, Activity, LiveOps, Music, Moderation];

    public static bool IsValid(string eventClass) =>
        All.Contains(eventClass, StringComparer.Ordinal);

    /// <summary>The always-on base group every joined connection is in.</summary>
    public static string BaseGroup(string broadcasterId) => $"channel-{broadcasterId}";

    /// <summary>The per-class group a subscribed connection additionally joins.</summary>
    public static string ClassGroup(string broadcasterId, string eventClass) =>
        $"channel-{broadcasterId}:{eventClass}";

    /// <summary>
    /// Routes a generic <c>ChannelEvent</c> method key to its class: the live-ops surfaces
    /// (polls/predictions/hype train/ad breaks) and moderation transitions have their own classes;
    /// everything else (follows/subs/cheers/raids/shoutouts/role changes/…) is activity-feed traffic.
    /// </summary>
    public static string ClassForChannelEvent(string method)
    {
        if (
            method.StartsWith("poll_", StringComparison.Ordinal)
            || method.StartsWith("prediction_", StringComparison.Ordinal)
            || method.StartsWith("hype_train_", StringComparison.Ordinal)
            || method.StartsWith("ad_break_", StringComparison.Ordinal)
        )
            return LiveOps;

        if (
            method is "chat_cleared" or "message_deleted"
            || method.StartsWith("shield_mode_", StringComparison.Ordinal)
        )
            return Moderation;

        return Activity;
    }
}
