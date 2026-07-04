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
    public void BuildCondition_ModeratorPlaneTopics_CarryBroadcasterAndModeratorUserId(
        string eventType
    )
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
                    ["moderator_user_id"] = BotTwitchId,
                }
            );
    }

    [Fact]
    public void BuildCondition_ModeratorPlaneTopic_FallsBackToBroadcasterId_WhenNoDedicatedBot()
    {
        // Self-host with no registered bot account: the streamer IS the bot, so moderator_user_id falls back
        // to the broadcaster's own Twitch id rather than staying null/empty.
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

    [Theory]
    [InlineData("user.update")]
    [InlineData("user.whisper.message")]
    public void BuildCondition_UserPlaneTopics_CarryOnlyUserId(string eventType)
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
            .Be(new KeyValuePair<string, string>("user_id", BotTwitchId));
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

    [Fact]
    public void GetVersion_NeverReturnsBeta_GuestStarIsNotASubscribableSurface()
    {
        // Guest Star is deprecated by Twitch and deliberately excluded — no topic in this codebase should ever
        // resolve to the old "beta" version string.
        string[] everyKnownTopic =
        [
            "channel.update",
            "channel.moderate",
            "channel.guest_star_session.begin",
            "channel.guest_star_session.end",
            "channel.guest_star_guest.update",
            "channel.guest_star_settings.update",
        ];

        foreach (string eventType in everyKnownTopic)
            Builder.GetVersion(eventType).Should().NotBe("beta");
    }

    // ── Token owner ──────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("channel.update")]
    [InlineData("channel.moderate")]
    [InlineData("channel.chat.notification")]
    [InlineData("user.update")]
    [InlineData("user.whisper.message")]
    public void RequiresBroadcasterToken_IsAlwaysFalse_EveryCreateRidesTheBotToken(string eventType)
    {
        // Multi-tenant WebSocket EventSub requires every subscription POST on one session to come from the
        // same Twitch user; this codebase satisfies that by always using the bot/app token, never the
        // broadcaster's, regardless of topic.
        Builder.RequiresBroadcasterToken(eventType).Should().BeFalse();
    }
}
