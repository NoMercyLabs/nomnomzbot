// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json.Nodes;
using FluentAssertions;
using NomNomzBot.Infrastructure.DevPlatform;

namespace NomNomzBot.Infrastructure.Tests.DevPlatform;

/// <summary>
/// Pins that the wire fixtures carry every field Twitch sends, as listed in
/// <c>.claude/docs/design/ledgers/eventsub-fixtures-vs-docs.md</c>. A path under a null value counts as present:
/// Twitch sends the variant objects it does not use as null.
/// </summary>
public sealed class EventSampleFieldCoverageTests
{
    private static readonly IReadOnlyDictionary<string, string[]> RequiredPaths = new Dictionary<
        string,
        string[]
    >
    {
        ["chat.message"] =
        [
            "badges[]/id",
            "badges[]/info",
            "badges[]/set_id",
            "broadcaster_user_login",
            "broadcaster_user_name",
            "channel_points_custom_reward_id",
            "cheer",
            "message/fragments[]/cheermote",
            "message/fragments[]/emote",
            "message/fragments[]/mention",
            "reply",
            "source_badges",
            "source_broadcaster_user_id",
            "source_broadcaster_user_login",
            "source_broadcaster_user_name",
            "source_message_id",
        ],
        ["chat.message.deleted"] = ["broadcaster_user_login", "broadcaster_user_name"],
        ["chat.user.messages.cleared"] = ["broadcaster_user_login", "broadcaster_user_name"],
        ["chat.notification"] =
        [
            "announcement",
            "badges",
            "bits_badge_tier",
            "broadcaster_user_login",
            "broadcaster_user_name",
            "charity_donation",
            "color",
            "community_sub_gift",
            "gift_paid_upgrade",
            "gifted_drops_summary",
            "is_source_only",
            "modiversary",
            "pay_it_forward",
            "prime_paid_upgrade",
            "raid",
            "resub",
            "resub/cumulative_months",
            "resub/duration_months",
            "resub/gifter_is_anonymous",
            "resub/gifter_user_id",
            "resub/gifter_user_login",
            "resub/gifter_user_name",
            "resub/is_gift",
            "resub/streak_months",
            "resub/sub_plan",
            "shared_chat_announcement",
            "shared_chat_community_sub_gift",
            "shared_chat_gift_paid_upgrade",
            "shared_chat_gifted_drops_summary",
            "shared_chat_modiversary",
            "shared_chat_pay_it_forward",
            "shared_chat_prime_paid_upgrade",
            "shared_chat_raid",
            "shared_chat_resub",
            "shared_chat_sub",
            "shared_chat_sub_gift",
            "source_badges",
            "source_broadcaster_user_id",
            "source_broadcaster_user_login",
            "source_broadcaster_user_name",
            "source_message_id",
            "sub",
            "sub_gift",
            "unraid",
            "watch_streak",
        ],
        ["chat.settings.updated"] = ["broadcaster_user_login", "broadcaster_user_name"],
        ["chat.user.message.held"] =
        [
            "broadcaster_user_login",
            "broadcaster_user_name",
            "message/fragments[]/cheermote",
            "message/fragments[]/emote",
            "message/fragments[]/emote/emote_set_id",
            "message/fragments[]/emote/id",
        ],
        ["chat.user.message.updated"] =
        [
            "broadcaster_user_login",
            "broadcaster_user_name",
            "message/fragments[]/cheermote",
            "message/fragments[]/emote",
            "message/fragments[]/emote/emote_set_id",
            "message/fragments[]/emote/id",
        ],
        ["rewards.watch.streak.received"] =
        [
            "announcement",
            "badges",
            "bits_badge_tier",
            "broadcaster_user_login",
            "broadcaster_user_name",
            "charity_donation",
            "color",
            "community_sub_gift",
            "gift_paid_upgrade",
            "gifted_drops_summary",
            "is_source_only",
            "modiversary",
            "pay_it_forward",
            "prime_paid_upgrade",
            "raid",
            "resub",
            "resub/cumulative_months",
            "resub/duration_months",
            "resub/gifter_is_anonymous",
            "resub/gifter_user_id",
            "resub/gifter_user_login",
            "resub/gifter_user_name",
            "resub/is_gift",
            "resub/streak_months",
            "resub/sub_plan",
            "shared_chat_announcement",
            "shared_chat_community_sub_gift",
            "shared_chat_gift_paid_upgrade",
            "shared_chat_gifted_drops_summary",
            "shared_chat_modiversary",
            "shared_chat_pay_it_forward",
            "shared_chat_prime_paid_upgrade",
            "shared_chat_raid",
            "shared_chat_resub",
            "shared_chat_sub",
            "shared_chat_sub_gift",
            "source_badges",
            "source_broadcaster_user_id",
            "source_broadcaster_user_login",
            "source_broadcaster_user_name",
            "source_message_id",
            "sub",
            "sub_gift",
            "unraid",
        ],
        ["chat.shared.chat.updated"] =
        [
            "broadcaster_user_id",
            "broadcaster_user_login",
            "broadcaster_user_name",
            "participants[]/broadcaster_user_login",
            "participants[]/broadcaster_user_name",
        ],
        ["chat.shared.chat.ended"] = ["broadcaster_user_login", "broadcaster_user_name"],
        ["moderation.auto.mod.message.updated"] =
        [
            "broadcaster_user_login",
            "broadcaster_user_name",
            "category",
            "fragments",
            "fragments/cheermotes",
            "fragments/cheermotes[]/amount",
            "fragments/cheermotes[]/prefix",
            "fragments/cheermotes[]/text",
            "fragments/cheermotes[]/tier",
            "fragments/emotes",
            "fragments/emotes[]/id",
            "fragments/emotes[]/set-id",
            "fragments/emotes[]/text",
            "level",
        ],
        ["moderation.auto.mod.settings.updated"] =
        [
            "data",
            "data[]/aggression",
            "data[]/broadcaster_user_id",
            "data[]/broadcaster_user_login",
            "data[]/broadcaster_user_name",
            "data[]/bullying",
            "data[]/disability",
            "data[]/misogyny",
            "data[]/moderator_user_id",
            "data[]/moderator_user_login",
            "data[]/moderator_user_name",
            "data[]/overall_level",
            "data[]/race_ethnicity_or_religion",
            "data[]/sex_based_terms",
            "data[]/sexuality_sex_or_gender",
            "data[]/swearing",
        ],
        ["moderation.warning.acknowledged"] =
        [
            "broadcaster_user_id",
            "broadcaster_user_login",
            "broadcaster_user_name",
        ],
        ["moderation.warning.sent"] =
        [
            "broadcaster_user_id",
            "broadcaster_user_login",
            "broadcaster_user_name",
            "moderator_user_login",
        ],
        ["moderation.suspicious.user.message"] =
        [
            "broadcaster_user_login",
            "broadcaster_user_name",
            "message/fragments[]/cheermote",
            "message/fragments[]/emote",
            "message/fragments[]/emote/emote_set_id",
            "message/fragments[]/emote/id",
            "message/fragments[]/text",
            "message/fragments[]/type",
        ],
        ["moderation.suspicious.user.updated"] =
        [
            "broadcaster_user_login",
            "broadcaster_user_name",
            "moderator_user_login",
        ],
        ["moderation.shield.mode.began"] =
        [
            "broadcaster_user_login",
            "broadcaster_user_name",
            "moderator_user_login",
        ],
        ["moderation.shield.mode.ended"] =
        [
            "broadcaster_user_login",
            "broadcaster_user_name",
            "moderator_user_login",
        ],
        ["moderation.user.banned"] =
        [
            "broadcaster_user_login",
            "broadcaster_user_name",
            "moderator_user_login",
        ],
        ["moderation.user.timed.out"] =
        [
            "broadcaster_user_id",
            "broadcaster_user_login",
            "broadcaster_user_name",
            "moderator_user_login",
            "moderator_user_name",
        ],
        ["moderation.user.unbanned"] =
        [
            "broadcaster_user_id",
            "broadcaster_user_login",
            "broadcaster_user_name",
            "moderator_user_login",
        ],
        ["moderation.unban.request.created"] =
        [
            "broadcaster_user_id",
            "broadcaster_user_login",
            "broadcaster_user_name",
        ],
        ["moderation.unban.request.resolved"] =
        [
            "broadcaster_user_id",
            "broadcaster_user_login",
            "broadcaster_user_name",
            "moderator_user_login",
        ],
        ["moderation.moderator.added"] =
        [
            "broadcaster_user_id",
            "broadcaster_user_login",
            "broadcaster_user_name",
        ],
        ["moderation.moderator.removed"] =
        [
            "broadcaster_user_id",
            "broadcaster_user_login",
            "broadcaster_user_name",
        ],
        ["moderation.vip.added"] =
        [
            "broadcaster_user_id",
            "broadcaster_user_login",
            "broadcaster_user_name",
        ],
        ["moderation.vip.removed"] =
        [
            "broadcaster_user_id",
            "broadcaster_user_login",
            "broadcaster_user_name",
        ],
        ["moderation.action.taken"] =
        [
            "automod_terms",
            "broadcaster_user_login",
            "broadcaster_user_name",
            "mod",
            "raid",
            "shared_chat_ban",
            "shared_chat_delete",
            "shared_chat_timeout",
            "shared_chat_unban",
            "shared_chat_untimeout",
            "slow",
            "unban",
            "unban_request",
            "unmod",
            "unraid",
            "untimeout",
            "unvip",
            "vip",
        ],
        ["rewards.new.subscription"] = ["broadcaster_user_login", "broadcaster_user_name"],
        ["rewards.resubscription"] =
        [
            "broadcaster_user_id",
            "broadcaster_user_login",
            "broadcaster_user_name",
            "message/emotes[]/begin",
            "message/emotes[]/end",
            "message/emotes[]/id",
        ],
        ["rewards.gift.subscription"] =
        [
            "broadcaster_user_id",
            "broadcaster_user_login",
            "broadcaster_user_name",
        ],
        ["rewards.subscription.ended"] =
        [
            "broadcaster_user_id",
            "broadcaster_user_login",
            "broadcaster_user_name",
        ],
        ["rewards.cheer"] = ["broadcaster_user_login", "broadcaster_user_name"],
        ["rewards.bits.used"] = ["custom_power_up"],
        ["rewards.reward.redeemed"] = ["broadcaster_user_login", "broadcaster_user_name"],
        ["rewards.reward.redemption.updated"] = ["broadcaster_user_login", "broadcaster_user_name"],
        ["rewards.reward.created"] =
        [
            "background_color",
            "cooldown_expires_at",
            "default_image",
            "default_image/url_1x",
            "default_image/url_2x",
            "default_image/url_4x",
            "global_cooldown",
            "global_cooldown/is_enabled",
            "global_cooldown/seconds",
            "image",
            "image/url_1x",
            "image/url_2x",
            "image/url_4x",
            "is_user_input_required",
            "max_per_stream",
            "max_per_stream/is_enabled",
            "max_per_stream/value",
            "max_per_user_per_stream",
            "max_per_user_per_stream/is_enabled",
            "max_per_user_per_stream/value",
            "redemptions_redeemed_current_stream",
            "should_redemptions_skip_request_queue",
        ],
        ["rewards.reward.updated"] =
        [
            "background_color",
            "broadcaster_user_id",
            "broadcaster_user_login",
            "broadcaster_user_name",
            "cooldown_expires_at",
            "default_image",
            "default_image/url_1x",
            "default_image/url_2x",
            "default_image/url_4x",
            "global_cooldown",
            "global_cooldown/is_enabled",
            "global_cooldown/seconds",
            "image",
            "image/url_1x",
            "image/url_2x",
            "image/url_4x",
            "is_in_stock",
            "is_paused",
            "is_user_input_required",
            "max_per_stream",
            "max_per_stream/is_enabled",
            "max_per_stream/value",
            "max_per_user_per_stream",
            "max_per_user_per_stream/is_enabled",
            "max_per_user_per_stream/value",
            "redemptions_redeemed_current_stream",
            "should_redemptions_skip_request_queue",
        ],
        ["rewards.reward.removed"] =
        [
            "background_color",
            "broadcaster_user_login",
            "broadcaster_user_name",
            "cooldown_expires_at",
            "default_image",
            "default_image/url_1x",
            "default_image/url_2x",
            "default_image/url_4x",
            "global_cooldown",
            "global_cooldown/is_enabled",
            "global_cooldown/seconds",
            "image",
            "image/url_1x",
            "image/url_2x",
            "image/url_4x",
            "is_in_stock",
            "is_paused",
            "is_user_input_required",
            "max_per_stream",
            "max_per_stream/is_enabled",
            "max_per_stream/value",
            "max_per_user_per_stream",
            "max_per_user_per_stream/is_enabled",
            "max_per_user_per_stream/value",
            "prompt",
            "redemptions_redeemed_current_stream",
            "should_redemptions_skip_request_queue",
        ],
        ["community.poll.began"] =
        [
            "bits_voting",
            "bits_voting/amount_per_vote",
            "bits_voting/is_enabled",
            "broadcaster_user_login",
            "broadcaster_user_name",
            "channel_points_voting",
            "channel_points_voting/amount_per_vote",
            "channel_points_voting/is_enabled",
        ],
        ["community.poll.progress"] =
        [
            "bits_voting",
            "bits_voting/amount_per_vote",
            "bits_voting/is_enabled",
            "broadcaster_user_id",
            "broadcaster_user_login",
            "broadcaster_user_name",
            "channel_points_voting",
            "channel_points_voting/amount_per_vote",
            "channel_points_voting/is_enabled",
            "choices[]/bits_votes",
            "started_at",
        ],
        ["community.poll.ended"] =
        [
            "bits_voting",
            "bits_voting/amount_per_vote",
            "bits_voting/is_enabled",
            "broadcaster_user_id",
            "broadcaster_user_login",
            "broadcaster_user_name",
            "channel_points_voting",
            "channel_points_voting/amount_per_vote",
            "channel_points_voting/is_enabled",
            "choices[]/bits_votes",
            "ended_at",
            "started_at",
        ],
        ["community.prediction.began"] =
        [
            "broadcaster_user_id",
            "broadcaster_user_login",
            "broadcaster_user_name",
        ],
        ["community.prediction.progress"] =
        [
            "broadcaster_user_id",
            "broadcaster_user_login",
            "broadcaster_user_name",
            "outcomes[]/top_predictors",
            "outcomes[]/top_predictors[]/channel_points_used",
            "outcomes[]/top_predictors[]/channel_points_won",
            "outcomes[]/top_predictors[]/user_id",
            "outcomes[]/top_predictors[]/user_login",
            "outcomes[]/top_predictors[]/user_name",
            "started_at",
        ],
        ["community.prediction.locked"] =
        [
            "broadcaster_user_id",
            "broadcaster_user_login",
            "broadcaster_user_name",
            "locked_at",
            "outcomes[]/top_predictors",
            "outcomes[]/top_predictors[]/channel_points_used",
            "outcomes[]/top_predictors[]/channel_points_won",
            "outcomes[]/top_predictors[]/user_id",
            "outcomes[]/top_predictors[]/user_login",
            "outcomes[]/top_predictors[]/user_name",
            "started_at",
        ],
        ["community.prediction.ended"] =
        [
            "broadcaster_user_id",
            "broadcaster_user_login",
            "broadcaster_user_name",
            "outcomes[]/top_predictors",
            "outcomes[]/top_predictors[]/channel_points_used",
            "outcomes[]/top_predictors[]/channel_points_won",
            "outcomes[]/top_predictors[]/user_id",
            "outcomes[]/top_predictors[]/user_login",
            "outcomes[]/top_predictors[]/user_name",
        ],
        ["community.hype.train.began"] =
        [
            "all_time_high_level",
            "all_time_high_total",
            "broadcaster_user_login",
            "broadcaster_user_name",
            "is_shared_train",
            "shared_train_participants",
            "type",
        ],
        ["community.hype.train.progress"] =
        [
            "broadcaster_user_id",
            "broadcaster_user_login",
            "broadcaster_user_name",
            "is_shared_train",
            "shared_train_participants",
            "started_at",
            "type",
        ],
        ["community.hype.train.ended"] =
        [
            "broadcaster_user_id",
            "broadcaster_user_login",
            "broadcaster_user_name",
            "cooldown_ends_at",
            "is_shared_train",
            "shared_train_participants",
            "type",
        ],
        ["community.goal.began"] = ["broadcaster_user_login", "broadcaster_user_name"],
        ["community.goal.progress"] =
        [
            "broadcaster_user_id",
            "broadcaster_user_login",
            "broadcaster_user_name",
        ],
        ["community.goal.ended"] =
        [
            "broadcaster_user_id",
            "broadcaster_user_login",
            "broadcaster_user_name",
        ],
        ["community.charity.campaign.started"] =
        [
            "broadcaster_id",
            "broadcaster_login",
            "broadcaster_name",
        ],
        ["community.charity.campaign.progress"] =
        [
            "broadcaster_id",
            "broadcaster_login",
            "broadcaster_name",
            "charity_description",
            "charity_logo",
            "charity_website",
        ],
        ["community.charity.donation"] =
        [
            "broadcaster_user_login",
            "broadcaster_user_name",
            "charity_description",
            "charity_logo",
            "charity_website",
        ],
        ["community.charity.campaign.stopped"] =
        [
            "broadcaster_id",
            "broadcaster_login",
            "broadcaster_name",
            "charity_description",
            "charity_logo",
            "charity_website",
        ],
        ["stream.offline"] = ["id"],
        ["stream.shoutout.sent"] = ["broadcaster_user_login", "broadcaster_user_name"],
        ["stream.shoutout.received"] = ["broadcaster_user_login", "broadcaster_user_name"],
        ["stream.guest.star.session.began"] =
        [
            "moderator_user_id",
            "moderator_user_login",
            "moderator_user_name",
        ],
        ["stream.guest.star.session.ended"] =
        [
            "broadcaster_user_login",
            "broadcaster_user_name",
            "moderator_user_id",
            "moderator_user_login",
            "moderator_user_name",
        ],
        ["stream.guest.star.guest.updated"] = ["broadcaster_user_login", "broadcaster_user_name"],
        ["stream.guest.star.settings.updated"] =
        [
            "broadcaster_user_login",
            "broadcaster_user_name",
        ],
    };

    [Fact]
    public void Fixtures_carry_every_field_the_twitch_docs_list()
    {
        List<string> missing = [];
        foreach (KeyValuePair<string, string[]> entry in RequiredPaths)
        {
            JsonNode root = JsonNode.Parse(EventSamplePayloads.ByWireName[entry.Key])!;
            missing.AddRange(
                entry
                    .Value.Where(path => !HasPath(root, path.Split('/'), 0))
                    .Select(path => $"{entry.Key}: {path}")
            );
        }

        missing.Should().BeEmpty("each fixture must carry every field the Twitch docs list");
    }

    private static bool HasPath(JsonNode? node, string[] segments, int index)
    {
        if (index == segments.Length)
            return true;

        bool isArray = segments[index].EndsWith("[]", StringComparison.Ordinal);
        string name = isArray ? segments[index][..^2] : segments[index];
        if (node is not JsonObject obj || !obj.TryGetPropertyValue(name, out JsonNode? child))
            return false;

        if (isArray)
            return child is JsonArray { Count: > 0 } items
                && items.All(item => HasPath(item, segments, index + 1));

        return child is null || HasPath(child, segments, index + 1);
    }
}
