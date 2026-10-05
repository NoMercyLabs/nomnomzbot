// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Infrastructure.Content.Widgets;
using NomNomzBot.Infrastructure.Widgets.EventHandlers;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// BUILD-TODO's "the event clicker doesn't reflect the actual widget" report: a widget's fire-bar button list
/// is driven by its REAL declared <c>EventSubscriptions</c> (fixed separately — S060-remaining), but the sample
/// PAYLOAD for a declared-yet-uncovered event type silently fell back to the generic <c>{ user }</c> placeholder.
/// For a handler that guards on a specific field (chat_box.vue's <c>onEnriched</c> returns without <c>title</c>,
/// <c>onMessageDeleted</c> without <c>messageId</c>, now_playing.vue's pulse without <c>isSaved</c>), that made a
/// real, genuinely-subscribed button a silent no-op — the click did nothing, which reads exactly like "the
/// rendered widget does not reflect the actual widget".
///
/// <para>
/// This is the completeness guard: every event type any first-party widget actually declares must have a
/// sample that is not the bare fallback, so a newly-declared subscription with no matching sample fails loudly
/// here instead of shipping a dead test button.
/// </para>
/// </summary>
public sealed class WidgetTestSamplesCompletenessTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string Shape(string eventType) =>
        JsonSerializer.Serialize(WidgetTestSamples.For(eventType, DateTimeOffset.UnixEpoch), Json);

    [Fact]
    public void Every_event_type_declared_by_a_first_party_widget_has_a_real_sample()
    {
        string fallbackShape = Shape("__no_widget_declares_this_event_type__");

        List<string> declared = FirstPartyWidgetCatalogue
            .All.SelectMany(widget => widget.DefaultEventSubscriptions)
            .Distinct()
            .ToList();
        declared.Should().NotBeEmpty();

        List<string> stillFallingBackToTheGenericDefault = declared
            .Where(eventType => Shape(eventType) == fallbackShape)
            .ToList();

        stillFallingBackToTheGenericDefault
            .Should()
            .BeEmpty(
                "a widget that genuinely declares this event gets a real fire-bar/test button, and firing it "
                    + "with the bare {{ user }} placeholder silently no-ops any handler that reads a "
                    + "different field — exactly the 'event clicker doesn't reflect the actual widget' report"
            );
    }

    /// <summary>
    /// Every event type a live handler sends to widgets, with the payload type that handler sends (read from
    /// <c>Hubs/Broadcasters/*</c> and <c>Infrastructure/Widgets/EventHandlers/*</c>). A sample built from that
    /// same type carries every live field by construction; a hand-shaped sample drifted (the follow sample had
    /// only <c>user</c>, the live follow also carries login, avatar, pronouns and standing).
    /// </summary>
    public static TheoryData<string, Type> LivePayloadTypes =>
        new()
        {
            { "follow", typeof(FollowAlertDto) },
            { "subscription", typeof(SubscriptionAlertDto) },
            { "resub", typeof(ResubAlertDto) },
            { "gift", typeof(GiftSubAlertDto) },
            { "cheer", typeof(CheerAlertDto) },
            { "raid", typeof(RaidAlertDto) },
            { "ban", typeof(ModActionDto) },
            { "timeout", typeof(ModActionDto) },
            { "unban", typeof(ModActionDto) },
            { "ChatMessage", typeof(DashboardChatMessageDto) },
            { "ChatCleared", typeof(ChatClearedDto) },
            { "MessageDeleted", typeof(MessageDeletedDto) },
            { "UserMessagesCleared", typeof(UserMessagesClearedDto) },
            { "ChatMessageEnriched", typeof(ChatMessageEnrichedWidgetPayload) },
            { "hype_train_begin", typeof(HypeTrainBeganAlertDto) },
            { "hype_train_progress", typeof(HypeTrainProgressAlertDto) },
            { "hype_train_end", typeof(HypeTrainEndedAlertDto) },
            { "poll_begin", typeof(PollBeganAlertDto) },
            { "poll_progress", typeof(PollProgressAlertDto) },
            { "poll_end", typeof(PollEndedAlertDto) },
            { "prediction_begin", typeof(PredictionBeganAlertDto) },
            { "prediction_progress", typeof(PredictionProgressAlertDto) },
            { "prediction_lock", typeof(PredictionLockedAlertDto) },
            { "prediction_end", typeof(PredictionEndedAlertDto) },
            { "reward_redeemed", typeof(RewardRedeemedDto) },
            { "moderator_added", typeof(RoleChangedAlertDto) },
            { "moderator_removed", typeof(RoleChangedAlertDto) },
            { "vip_added", typeof(RoleChangedAlertDto) },
            { "vip_removed", typeof(RoleChangedAlertDto) },
            { "shoutout_sent", typeof(ShoutoutSentAlertDto) },
            { "shoutout_received", typeof(ShoutoutReceivedAlertDto) },
            { "sr_queue", typeof(SrQueueWidgetPayload) },
            { "now_playing", typeof(NowPlayingWidgetPayload) },
            { "ad_schedule", typeof(AdScheduleWidgetPayload) },
            { "ad_upcoming", typeof(AdUpcomingWidgetPayload) },
            { "track_saved_changed", typeof(TrackSavedWidgetPayload) },
            { "tts_speak", typeof(TtsSpeakWidgetPayload) },
            { "goal", typeof(GoalWidgetEventPayload) },
            { "supporter.tip", typeof(SupporterAlertPayload) },
            { "supporter.membership", typeof(SupporterAlertPayload) },
            { "supporter.merch", typeof(SupporterAlertPayload) },
            { "supporter.charity", typeof(SupporterAlertPayload) },
            { "voice_trigger", typeof(VoiceTriggerWidgetEventPayload) },
            { "custom.heartrate", typeof(CustomDataWidgetPayload) },
        };

    [Theory]
    [MemberData(nameof(LivePayloadTypes))]
    public void The_sample_is_built_from_the_type_the_live_event_sends(
        string eventType,
        Type liveType
    )
    {
        WidgetTestSamples.For(eventType, DateTimeOffset.UnixEpoch).Should().BeOfType(liveType);
    }

    [Fact]
    public void ChatMessageEnriched_sample_carries_a_title_so_chat_box_onEnriched_does_not_drop_it()
    {
        // chat_box.vue: `function onEnriched(e) { if (!e.title) return }` — named explicitly because it is
        // the exact defect reported (song-request card button rendered nothing).
        JsonElement payload = JsonSerializer.SerializeToElement(
            WidgetTestSamples.For("ChatMessageEnriched", DateTimeOffset.UnixEpoch),
            Json
        );

        payload.GetProperty("title").GetString().Should().NotBeNullOrEmpty();
    }
}
