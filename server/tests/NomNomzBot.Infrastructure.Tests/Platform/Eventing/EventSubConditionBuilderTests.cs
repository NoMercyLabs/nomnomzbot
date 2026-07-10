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
using NomNomzBot.Infrastructure.Platform.Eventing;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// Proves the per-topic create facts <see cref="EventSubConditionBuilder"/> hands to
/// <c>TwitchEventSubHostedService.SubscribeAsync</c> for the newly-subscribed topics (E1: subscribe every
/// remaining translator-backed topic) are exactly what Twitch's EventSub reference documents — the condition
/// object's field set, the topic version, and (via <see cref="EventSubConditionBuilder.RequiresBroadcasterToken"/>)
/// the token owner. A wrong shape here is a silent 400 from Twitch at subscribe time, not a compile error, so
/// each condition shape class gets a representative topic asserted field-by-field.
/// <para>
/// Also proves Guest Star ingest is restored (ROADMAP "Small decided items" — the E1 commit's "Twitch
/// deprecated it" claim was false against live docs, which still list all four topics as <c>beta</c>, not
/// removed): its four topics carry the same broadcaster+moderator condition shape as the other moderator-plane
/// topics, and <see cref="EventSubConditionBuilder.GetVersion"/> resolves them to <c>"beta"</c>.
/// </para>
/// </summary>
public sealed class EventSubConditionBuilderTests
{
    private const string BroadcasterTwitchId = "twitch-broadcaster-1";
    private const string BotTwitchId = "twitch-bot-9";

    private static readonly EventSubConditionBuilder Builder = new();

    // ── BroadcasterOnly shape: { broadcaster_user_id } ──────────────────────────────────────────────

    [Theory]
    [InlineData("channel.update")]
    [InlineData("channel.hype_train.progress")]
    [InlineData("channel.poll.progress")]
    [InlineData("channel.prediction.progress")]
    [InlineData("channel.channel_points_custom_reward_redemption.update")]
    [InlineData("channel.channel_points_custom_reward.add")]
    [InlineData("channel.channel_points_custom_reward.update")]
    [InlineData("channel.channel_points_custom_reward.remove")]
    [InlineData("channel.channel_points_automatic_reward_redemption.add")]
    [InlineData("channel.custom_power_up_redemption.add")]
    [InlineData("channel.unban")]
    [InlineData("channel.moderator.add")]
    [InlineData("channel.moderator.remove")]
    [InlineData("channel.vip.add")]
    [InlineData("channel.vip.remove")]
    [InlineData("channel.ad_break.begin")]
    [InlineData("channel.bits.use")]
    [InlineData("channel.shared_chat.begin")]
    [InlineData("channel.shared_chat.update")]
    [InlineData("channel.shared_chat.end")]
    [InlineData("channel.subscription.end")]
    public void BuildCondition_BroadcasterOnlyTopics_CarryOnlyBroadcasterUserId(string eventType)
    {
        IReadOnlyDictionary<string, string> condition = Builder.BuildCondition(
            eventType,
            BroadcasterTwitchId,
            BotTwitchId
        );

        condition
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(new KeyValuePair<string, string>("broadcaster_user_id", BroadcasterTwitchId));
    }

    // ── BroadcasterAndModerator shape: { broadcaster_user_id, moderator_user_id } ───────────────────

    [Theory]
    [InlineData("channel.moderate")]
    [InlineData("channel.unban_request.create")]
    [InlineData("channel.unban_request.resolve")]
    [InlineData("channel.warning.acknowledge")]
    [InlineData("channel.warning.send")]
    [InlineData("channel.suspicious_user.message")]
    [InlineData("channel.suspicious_user.update")]
    [InlineData("channel.shield_mode.begin")]
    [InlineData("channel.shield_mode.end")]
    [InlineData("channel.shoutout.create")]
    [InlineData("channel.shoutout.receive")]
    [InlineData("automod.message.hold")]
    [InlineData("automod.message.update")]
    [InlineData("automod.settings.update")]
    [InlineData("automod.terms.update")]
    [InlineData("channel.guest_star_session.begin")]
    [InlineData("channel.guest_star_session.end")]
    [InlineData("channel.guest_star_guest.update")]
    [InlineData("channel.guest_star_settings.update")]
    public void BuildCondition_ModeratorPlaneTopics_CarryBroadcasterAndModeratorUserId(
        string eventType
    )
    {
        IReadOnlyDictionary<string, string> condition = Builder.BuildCondition(
            eventType,
            BroadcasterTwitchId,
            BotTwitchId
        );

        // Moderator-plane topics are authorized by the BROADCASTER's token (it holds the moderator:* scopes and
        // the broadcaster is implicitly a moderator of their own channel), so the broadcaster — not the bot —
        // fills the moderator_user_id slot even when a dedicated bot account exists.
        condition
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, string>
                {
                    ["broadcaster_user_id"] = BroadcasterTwitchId,
                    ["moderator_user_id"] = BroadcasterTwitchId,
                }
            );
    }

    [Fact]
    public void BuildCondition_ModeratorPlaneTopic_UsesBroadcasterId_WhenNoDedicatedBot()
    {
        // Self-host with no registered bot account: the streamer IS the bot, so moderator_user_id is the
        // broadcaster's own Twitch id (same as the dedicated-bot case for broadcaster-authorized topics).
        IReadOnlyDictionary<string, string> condition = Builder.BuildCondition(
            "channel.shoutout.create",
            BroadcasterTwitchId,
            botTwitchUserId: null
        );

        condition
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, string>
                {
                    ["broadcaster_user_id"] = BroadcasterTwitchId,
                    ["moderator_user_id"] = BroadcasterTwitchId,
                }
            );
    }

    // ── BroadcasterAndUser shape: { broadcaster_user_id, user_id } ──────────────────────────────────

    [Theory]
    [InlineData("channel.chat.notification")]
    [InlineData("channel.chat.message_delete")]
    [InlineData("channel.chat.clear")]
    [InlineData("channel.chat.clear_user_messages")]
    [InlineData("channel.chat_settings.update")]
    [InlineData("channel.chat.user_message_hold")]
    [InlineData("channel.chat.user_message_update")]
    public void BuildCondition_ChatReadTopics_CarryBroadcasterAndReadingUserId(string eventType)
    {
        IReadOnlyDictionary<string, string> condition = Builder.BuildCondition(
            eventType,
            BroadcasterTwitchId,
            BotTwitchId
        );

        condition
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, string>
                {
                    ["broadcaster_user_id"] = BroadcasterTwitchId,
                    ["user_id"] = BotTwitchId,
                }
            );
    }

    // ── UserOnly shape: { user_id } ──────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildCondition_UserWhisper_CarriesTheBotUserId()
    {
        // The bot reads its OWN whisper inbox under its own user token, so user.whisper.message keys on the bot.
        IReadOnlyDictionary<string, string> condition = Builder.BuildCondition(
            "user.whisper.message",
            BroadcasterTwitchId,
            BotTwitchId
        );

        condition
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(new KeyValuePair<string, string>("user_id", BotTwitchId));
    }

    [Fact]
    public void BuildCondition_UserUpdate_CarriesTheBroadcasterUserId()
    {
        // user.update is the broadcaster's own profile-change feed, authorized by the broadcaster's token, so it
        // keys on the broadcaster — not the bot.
        IReadOnlyDictionary<string, string> condition = Builder.BuildCondition(
            "user.update",
            BroadcasterTwitchId,
            BotTwitchId
        );

        condition
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(new KeyValuePair<string, string>("user_id", BroadcasterTwitchId));
    }

    // ── Versions ─────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("channel.update", "2")]
    [InlineData("channel.moderate", "2")]
    [InlineData("channel.hype_train.progress", "2")]
    [InlineData("automod.message.hold", "2")]
    [InlineData("automod.message.update", "2")]
    [InlineData("channel.channel_points_automatic_reward_redemption.add", "2")]
    [InlineData("channel.poll.progress", "1")]
    [InlineData("channel.prediction.progress", "1")]
    [InlineData("channel.channel_points_custom_reward_redemption.update", "1")]
    [InlineData("channel.channel_points_custom_reward.add", "1")]
    [InlineData("channel.custom_power_up_redemption.add", "1")]
    [InlineData("channel.unban", "1")]
    [InlineData("channel.unban_request.create", "1")]
    [InlineData("channel.moderator.add", "1")]
    [InlineData("channel.vip.add", "1")]
    [InlineData("channel.warning.acknowledge", "1")]
    [InlineData("channel.suspicious_user.message", "1")]
    [InlineData("channel.shield_mode.begin", "1")]
    [InlineData("channel.shoutout.create", "1")]
    [InlineData("channel.ad_break.begin", "1")]
    [InlineData("channel.bits.use", "1")]
    [InlineData("user.update", "1")]
    [InlineData("user.whisper.message", "1")]
    [InlineData("channel.shared_chat.begin", "1")]
    [InlineData("channel.subscription.end", "1")]
    [InlineData("automod.settings.update", "1")]
    [InlineData("automod.terms.update", "1")]
    public void GetVersion_ReturnsTheDocumentedVersion(string eventType, string expectedVersion)
    {
        Builder.GetVersion(eventType).Should().Be(expectedVersion);
    }

    [Theory]
    [InlineData("channel.guest_star_session.begin")]
    [InlineData("channel.guest_star_session.end")]
    [InlineData("channel.guest_star_guest.update")]
    [InlineData("channel.guest_star_settings.update")]
    public void GetVersion_ReturnsBeta_ForGuestStarTopics(string eventType)
    {
        // Guest Star's EventSub topics are still documented by Twitch as "beta" (not deprecated, not removed) —
        // this is the one surface where the version string is genuinely "beta" rather than "1" or "2".
        Builder.GetVersion(eventType).Should().Be("beta");
    }

    [Fact]
    public void GetVersion_OnlyGuestStarTopicsResolveToBeta()
    {
        // Regression guard for the inverse of the theory above: no non-Guest-Star topic should ever pick up
        // the "beta" version string by accident (e.g. a copy/paste into the switch expression).
        string[] nonGuestStarTopics =
        [
            "channel.update",
            "channel.moderate",
            "channel.hype_train.progress",
            "automod.message.hold",
            "channel.channel_points_automatic_reward_redemption.add",
            "channel.chat.notification",
            "user.update",
        ];

        foreach (string eventType in nonGuestStarTopics)
            Builder.GetVersion(eventType).Should().NotBe("beta");
    }

    // ── Token owner ──────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("channel.update")]
    [InlineData("channel.moderate")]
    [InlineData("channel.subscribe")]
    [InlineData("channel.cheer")]
    [InlineData("channel.follow")]
    [InlineData("stream.online")]
    [InlineData("user.update")]
    [InlineData("channel.guest_star_session.begin")]
    public void RequiresBroadcasterToken_IsTrue_ForBroadcasterAuthorizedTopics(string eventType)
    {
        // Broadcaster-authorized topics ride the broadcaster's own token: it holds the channel/moderator scopes
        // the bot token lacks (else they 403), and each broadcaster gets its own per-token cost budget.
        Builder.RequiresBroadcasterToken(eventType).Should().BeTrue();
    }

    [Theory]
    [InlineData("channel.chat.message")]
    [InlineData("channel.chat.notification")]
    [InlineData("channel.chat.message_delete")]
    [InlineData("user.whisper.message")]
    public void RequiresBroadcasterToken_IsFalse_ForBotOwnedReadTopics(string eventType)
    {
        // The chat-read set and the bot's own whisper inbox are read under the bot's own user token.
        Builder.RequiresBroadcasterToken(eventType).Should().BeFalse();
    }

    // ── Cost-0 classification (drives cost-0-first subscribe ordering) ─────────────────────────────────

    [Theory]
    [InlineData("channel.chat.message")]
    [InlineData("channel.chat.notification")]
    [InlineData("channel.chat.message_delete")]
    [InlineData("channel.chat.clear")]
    [InlineData("channel.chat.clear_user_messages")]
    [InlineData("channel.chat_settings.update")]
    [InlineData("channel.chat.user_message_hold")]
    [InlineData("channel.chat.user_message_update")]
    public void IsCost0Topic_IsTrue_ForTheChatReadSet(string eventType)
    {
        // The chat-read topics the bot subscribes under its own authorized user:read:chat identity are charged
        // 0 by Twitch, so they never count against the WebSocket max_total_cost cap and must be subscribed first.
        EventSubConditionBuilder.IsCost0Topic(eventType).Should().BeTrue();
    }

    [Theory]
    [InlineData("channel.follow")]
    [InlineData("channel.subscribe")]
    [InlineData("channel.update")]
    [InlineData("channel.moderate")]
    [InlineData("channel.raid")]
    [InlineData("channel.cheer")]
    [InlineData("user.update")]
    public void IsCost0Topic_IsFalse_ForEveryOtherTopic(string eventType)
    {
        // Cost-1 topics (everything outside the chat-read set) do count against the cap and subscribe after.
        EventSubConditionBuilder.IsCost0Topic(eventType).Should().BeFalse();
    }
}
