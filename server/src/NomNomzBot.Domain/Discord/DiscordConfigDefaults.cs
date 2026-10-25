// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Discord;

/// <summary>
/// The one place each Discord setting's default is written. The entity initializers, the EF column
/// defaults, the create requests and the config services all read these values. A channel with no
/// Discord rule resolves to an empty list: no rule means no Discord post, by design.
/// </summary>
public static class DiscordConfigDefaults
{
    /// <summary>A new notification rule starts enabled.</summary>
    public const bool NotificationEnabled = true;

    /// <summary>A new live-role rule starts enabled.</summary>
    public const bool LiveRoleEnabled = true;

    /// <summary>The current shape version of the stored embed JSON. Only a breaking reshape raises it.</summary>
    public const int EmbedConfigVersion = 1;

    /// <summary>
    /// Every event the server fires a Discord post for, with its label and the placeholders its handler
    /// fills. Add a trigger here in the same change as its event handler, never before.
    /// </summary>
    public static readonly IReadOnlyList<DiscordNotificationTrigger> NotificationTriggerCatalog =
    [
        new(
            "go_live",
            "dashboard.discord.triggers.go_live",
            ["broadcaster", "channel.name", "title", "game"]
        ),
        new("follow", "dashboard.discord.triggers.follow", ["user.name", "channel.display"]),
        new(
            "subscription",
            "dashboard.discord.triggers.subscription",
            ["user.name", "tier", "channel.display"]
        ),
        new(
            "gift_subscription",
            "dashboard.discord.triggers.gift_subscription",
            ["user.name", "count", "tier", "channel.display"]
        ),
        new(
            "cheer",
            "dashboard.discord.triggers.cheer",
            ["user.name", "bits", "message", "channel.display"]
        ),
        new("raid", "dashboard.discord.triggers.raid", ["user.name", "viewers", "channel.display"]),
        new(
            "stream_end",
            "dashboard.discord.triggers.stream_end",
            ["broadcaster", "duration", "channel.name"]
        ),
        new(
            "resubscription",
            "dashboard.discord.triggers.resubscription",
            ["user.name", "tier", "months", "streak", "message", "channel.display"]
        ),
        new(
            "poll_end",
            "dashboard.discord.triggers.poll_end",
            ["poll.title", "poll.winner", "poll.winner.votes", "poll.results", "channel.display"]
        ),
        new(
            "prediction_resolved",
            "dashboard.discord.triggers.prediction_resolved",
            [
                "prediction.title",
                "prediction.winner",
                "prediction.winner.points",
                "prediction.winner.users",
                "channel.display",
            ]
        ),
    ];

    /// <summary>The events a notification rule can post for. The rule editor and the pipeline step offer only these.</summary>
    public static readonly IReadOnlyList<string> NotificationTriggers =
    [
        .. NotificationTriggerCatalog.Select(trigger => trigger.Name),
    ];

    /// <summary>
    /// Triggers older rules may already hold that no event handler raises yet. A saved rule keeps
    /// validating, but these are never offered for a new rule.
    /// </summary>
    public static readonly IReadOnlyList<string> UnfiredTriggers =
    [
        "new_clip",
        "schedule",
        "milestone",
    ];
}
