// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Discord.ValueObjects;

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
    /// The message a new <c>go_live</c> rule starts with when its creator sends none. The words match the
    /// dashboard's ready message, so a rule made through the API reads the same as one made on the page.
    /// </summary>
    public const string GoLiveMessage =
        "{channel.name} is live on Twitch streaming {game}, {title}. Come hang out: {channel.url}";

    /// <summary>
    /// The embed a new <c>go_live</c> rule starts with when its creator sends no message and no embed: the
    /// author line with the streamer's picture, the stream title linking to the channel, the game, the live
    /// preview picture, the send time, and a Watch Stream button.
    /// </summary>
    public static readonly DiscordEmbedConfig GoLiveEmbed = new(
        "{title}",
        null,
        "#9146FF",
        null,
        "{stream.thumbnail}",
        null,
        [new("Game", "{game}", true)],
        TitleUrl: "{channel.url}",
        AuthorName: "{channel.name} is now live on Twitch!",
        AuthorIconUrl: "{channel.avatar}",
        ShowTimestamp: true,
        ButtonLabel: "Watch Stream",
        ButtonUrl: "{channel.url}"
    );

    /// <summary>
    /// Every event the server fires a Discord post for, with its label and the placeholders its handler
    /// fills. Add a trigger here in the same change as its event handler, never before.
    /// </summary>
    public static readonly IReadOnlyList<DiscordNotificationTrigger> NotificationTriggerCatalog =
    [
        new(
            "go_live",
            "dashboard.discord.triggers.go_live",
            [
                "broadcaster",
                "channel.name",
                "title",
                "game",
                "channel.url",
                "channel.avatar",
                "stream.thumbnail",
            ]
        ),
        new("follow", "dashboard.discord.triggers.follow", ["user", "channel.display"]),
        new(
            "subscription",
            "dashboard.discord.triggers.subscription",
            ["user", "tier", "channel.display"]
        ),
        new(
            "gift_subscription",
            "dashboard.discord.triggers.gift_subscription",
            ["user", "count", "tier", "channel.display"]
        ),
        new(
            "cheer",
            "dashboard.discord.triggers.cheer",
            ["user", "bits", "message", "channel.display"]
        ),
        new("raid", "dashboard.discord.triggers.raid", ["user", "viewers", "channel.display"]),
        new(
            "stream_end",
            "dashboard.discord.triggers.stream_end",
            ["broadcaster", "duration", "channel.name"]
        ),
        new(
            "resubscription",
            "dashboard.discord.triggers.resubscription",
            ["user", "tier", "months", "streak", "message", "channel.display"]
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
