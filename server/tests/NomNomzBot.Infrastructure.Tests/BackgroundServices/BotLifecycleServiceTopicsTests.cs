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
/// asked of Twitch are now in the desired-subscribe set, and the set carries no duplicates.
/// </para>
/// <para>
/// Also proves Guest Star ingest is restored (ROADMAP "Small decided items" — the E1 commit's "Twitch
/// deprecated it" claim was false against live docs): its four <c>beta</c> topics are in the desired-subscribe
/// set even though <see cref="NomNomzBot.Infrastructure.Platform.Eventing.Translators.EventSubEventTranslator"/>
/// implementations for them were briefly deleted alongside it.
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

    // The 44 per-channel topics added by E1 — every translator-backed subscription type that was live but
    // never subscribed. E1 originally listed 45; user.whisper.message has since moved to the platform-plane
    // catalogue (one subscription per bot identity, not per channel) and is asserted separately below.
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
        "channel.shared_chat.begin",
        "channel.shared_chat.update",
        "channel.shared_chat.end",
        "channel.subscription.end",
        "automod.message.hold",
        "automod.message.update",
        "automod.settings.update",
        "automod.terms.update",
    ];

    // Guest Star ingest, restored after being wrongly deleted (ROADMAP "Small decided items").
    private static readonly string[] ExpectedGuestStarTopics =
    [
        "channel.guest_star_session.begin",
        "channel.guest_star_session.end",
        "channel.guest_star_guest.update",
        "channel.guest_star_settings.update",
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
    public void ChannelEventTypes_IncludesAllFortyFourPerChannelE1Topics()
    {
        ExpectedE1Topics
            .Should()
            .HaveCount(
                44,
                "the E1 brief enumerated 45 topics; user.whisper.message moved to the platform-plane catalogue"
            );
        BotLifecycleService.ChannelEventTypes.Should().Contain(ExpectedE1Topics);
    }

    [Fact]
    public void ChannelEventTypes_HasExactlySeventyThreeTopics()
    {
        // 25 pre-existing + 44 per-channel E1 topics + 4 restored Guest Star topics. A hard count catches a
        // silently-dropped or duplicated topic that the "Contain" assertions above would not (they only prove
        // a subset is present).
        BotLifecycleService.ChannelEventTypes.Should().HaveCount(73);
    }

    [Fact]
    public void WhisperInbox_IsPlatformPlane_NeverPerChannel()
    {
        // user.whisper.message's condition is the bot's own user id — identical for every channel — so a
        // per-channel subscribe could only 409 for every channel after the first. It must live exclusively in
        // the platform catalogue (subscribed once, tenant Guid.Empty); reappearing in ChannelEventTypes would
        // silently reintroduce the 409 parade and the first-channel-winner attribution.
        BotLifecycleService.PlatformEventTypes.Should().Equal("user.whisper.message");
        BotLifecycleService.ChannelEventTypes.Should().NotContain("user.whisper.message");
    }

    [Fact]
    public void ChannelEventTypes_IncludesAllFourGuestStarTopics()
    {
        // Guest Star ingest was wrongly deleted on a false "Twitch deprecated it" claim (live docs still list
        // all four beta topics, no deprecation notice) and has been restored.
        ExpectedGuestStarTopics.Should().HaveCount(4);
        BotLifecycleService.ChannelEventTypes.Should().Contain(ExpectedGuestStarTopics);
    }
}
