// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Infrastructure.BackgroundServices;

namespace NomNomzBot.Infrastructure.Tests.BackgroundServices;

/// <summary>
/// Proves the Charity/Goals EventSub ingest (ROADMAP "Small decided items") is actually wired into the
/// per-channel desired-subscribe set that <see cref="BotLifecycleService.SyncChannelsAsync"/> hands to
/// <c>ITwitchEventSubService.EnsureSubscribedAsync</c> on every channel join/reconcile — translators and
/// domain events for these topics existed already (commit 89d2b82); this is the piece that actually asks
/// Twitch to deliver them. Regresses (deleted/typo'd topic string) fail this test for the right reason: the
/// channel would silently stop receiving that topic even though the translator is fully wired to handle it.
/// <para>
/// Also proves E1 (subscribe every remaining translator-backed topic): the ~45 topics that already had a live
/// <see cref="NomNomzBot.Infrastructure.Platform.Eventing.Translators.EventSubEventTranslator"/> but were never
/// asked of Twitch are now in the desired-subscribe set, Guest Star is excluded (deprecated API), and the set
/// carries no duplicates.
/// </para>
/// </summary>
public sealed class BotLifecycleServiceTopicsTests
{
    private static readonly string[] ExpectedCharityAndGoalTopics =
    [
        "channel.charity_campaign.donate",
        "channel.charity_campaign.start",
        "channel.charity_campaign.progress",
        "channel.charity_campaign.stop",
        "channel.goal.begin",
        "channel.goal.progress",
        "channel.goal.end",
    ];

    // The 45 topics added by E1 — every translator-backed subscription type that was live but never subscribed.
    private static readonly string[] ExpectedE1Topics =
    [
        "channel.update",
        "channel.chat.notification",
        "channel.chat.message_delete",
        "channel.chat.clear",
        "channel.chat.clear_user_messages",
        "channel.chat_settings.update",
        "channel.chat.user_message_hold",
        "channel.chat.user_message_update",
        "channel.hype_train.progress",
        "channel.poll.progress",
        "channel.prediction.progress",
        "channel.channel_points_custom_reward_redemption.update",
        "channel.channel_points_custom_reward.add",
        "channel.channel_points_custom_reward.update",
        "channel.channel_points_custom_reward.remove",
        "channel.channel_points_automatic_reward_redemption.add",
        "channel.custom_power_up_redemption.add",
        "channel.moderate",
        "channel.unban",
        "channel.unban_request.create",
        "channel.unban_request.resolve",
        "channel.moderator.add",
        "channel.moderator.remove",
        "channel.vip.add",
        "channel.vip.remove",
        "channel.warning.acknowledge",
        "channel.warning.send",
        "channel.suspicious_user.message",
        "channel.suspicious_user.update",
        "channel.shield_mode.begin",
        "channel.shield_mode.end",
        "channel.shoutout.create",
        "channel.shoutout.receive",
        "channel.ad_break.begin",
        "channel.bits.use",
        "user.update",
        "user.whisper.message",
        "channel.shared_chat.begin",
        "channel.shared_chat.update",
        "channel.shared_chat.end",
        "channel.subscription.end",
        "automod.message.hold",
        "automod.message.update",
        "automod.settings.update",
        "automod.terms.update",
    ];

    [Fact]
    public void ChannelEventTypes_IncludesAllSevenCharityAndGoalTopics()
    {
        BotLifecycleService.ChannelEventTypes.Should().Contain(ExpectedCharityAndGoalTopics);
    }

    [Fact]
    public void ChannelEventTypes_HasNoDuplicateTopics()
    {
        // A duplicate would silently double-subscribe (or mask a copy/paste typo) — the registry's
        // (BroadcasterId, Provider, EventType, Version) unique index would swallow it, but it signals a bug here.
        BotLifecycleService.ChannelEventTypes.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ChannelEventTypes_StillIncludesThePreExistingHypeTrainTopics()
    {
        // Regression guard: adding the charity/goal block must not disturb the topics already relied on.
        BotLifecycleService
            .ChannelEventTypes.Should()
            .Contain([
                "channel.hype_train.begin",
                "channel.hype_train.end",
                "channel.chat.message",
            ]);
    }

    [Fact]
    public void ChannelEventTypes_IncludesAllFortyFiveE1Topics()
    {
        ExpectedE1Topics
            .Should()
            .HaveCount(45, "the E1 brief enumerates exactly this many topics to add");
        BotLifecycleService.ChannelEventTypes.Should().Contain(ExpectedE1Topics);
    }

    [Fact]
    public void ChannelEventTypes_HasExactlySeventyTopics()
    {
        // 25 pre-existing + 45 added by E1. A hard count catches a silently-dropped or duplicated topic that
        // the "Contain" assertions above would not (they only prove a subset is present).
        BotLifecycleService.ChannelEventTypes.Should().HaveCount(70);
    }

    [Fact]
    public void ChannelEventTypes_NeverIncludesGuestStarTopics()
    {
        // Twitch deprecated the Guest Star API — its EventSub topics must never be requested, even though the
        // translators for them still existed at some point in history.
        BotLifecycleService
            .ChannelEventTypes.Should()
            .NotContain(type => type.Contains("guest_star", StringComparison.Ordinal));
    }
}
