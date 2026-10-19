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
/// every class (the universal shell wants everything), <c>Subscribe</c> joins a subset.
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

    /// <summary>
    /// The Gate-2 read action a caller must hold to receive a class's pushes — the same right that guards the
    /// matching REST reads, so the live feed never shows more than the page would.
    /// </summary>
    public static string ReadActionFor(string eventClass) =>
        eventClass switch
        {
            Chat => "chat:read",
            Activity => "dashboard:read",
            LiveOps => "live-ops:polls:read",
            Music => "music:config:read",
            Moderation => "moderation:read",
            _ => throw new ArgumentOutOfRangeException(nameof(eventClass), eventClass, null),
        };

    public static bool IsValid(string eventClass) =>
        All.Contains(eventClass, StringComparer.Ordinal);

    /// <summary>The always-on base group every joined connection is in.</summary>
    public static string BaseGroup(string broadcasterId) => $"channel-{broadcasterId}";

    /// <summary>
    /// The viewer-safe music group any signed-in user may join: it carries only the pushes the public
    /// song-request page already shows (queue + now-playing), never the gated <c>music</c> class.
    /// </summary>
    public static string ViewerMusicGroup(string broadcasterId) => $"viewer-music:{broadcasterId}";

    /// <summary>The channel events mirrored into <see cref="ViewerMusicGroup"/>.</summary>
    public static bool IsViewerMusicEvent(string method) =>
        method is "sr_queue_changed" or "music.nowPlaying";

    /// <summary>The per-class group a subscribed connection additionally joins.</summary>
    public static string ClassGroup(string broadcasterId, string eventClass) =>
        $"channel-{broadcasterId}:{eventClass}";

    /// <summary>
    /// Routes a generic <c>ChannelEvent</c> method key to its class: the live-ops surfaces
    /// (polls/predictions/hype train/ad breaks), the song-request queue and moderation transitions have their own classes;
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

        // The queue snapshot is music-page data, read with the same right as the queue itself.
        if (IsViewerMusicEvent(method))
            return Music;

        if (
            method is "chat_cleared" or "message_deleted"
            || method.StartsWith("shield_mode_", StringComparison.Ordinal)
        )
            return Moderation;

        return Activity;
    }
}
