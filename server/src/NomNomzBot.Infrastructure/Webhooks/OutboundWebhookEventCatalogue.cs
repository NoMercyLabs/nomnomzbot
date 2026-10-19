// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Domain.Webhooks.Events;

namespace NomNomzBot.Infrastructure.Webhooks;

/// <summary>
/// The authoritative set of subscribable business events for outbound webhooks (webhooks.md §9). An endpoint's
/// <c>SubscribedEventTypes</c> is validated against this list so a subscription is a checklist of real, curated
/// event types rather than a free-text string. Each entry's <see cref="OutboundWebhookEventCatalogueEntry.EventType"/>
/// is the exact journaled discriminator (the CLR domain-event type name, e.g. <c>FollowEvent</c>) the fan-out matches
/// against <c>EventRecord.EventType</c> — so every entry is a real <c>DomainEventBase</c> type (guarded by test).
/// <para>
/// This roster reuses <c>OverlayEventFilter.UserFacingBusinessEvents</c> as its seed (the same curated user-facing
/// set, not a parallel copy) and extends it with the other clearly-safe business families. It EXCLUDES the
/// webhook-lifecycle events (<see cref="LifecycleDenyList"/>) — subscribing to those would make each delivery emit a
/// lifecycle event that re-matches and re-enqueues, an unbounded self-amplifying cascade (§9).
/// </para>
/// </summary>
public static class OutboundWebhookEventCatalogue
{
    /// <summary>The <c>*</c> subscription token — "all subscribable business events" (never a lifecycle event).</summary>
    public const string Wildcard = "*";

    /// <summary>
    /// The hard §9 deny-list: the webhook subsystem's own lifecycle events. These are never in <see cref="Entries"/>,
    /// <c>*</c> never matches them, and an explicit subscription to one is rejected at endpoint save.
    /// </summary>
    public static IReadOnlySet<string> LifecycleDenyList { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(OutboundWebhookEnqueuedEvent),
            nameof(OutboundWebhookAttemptedEvent),
            nameof(OutboundWebhookAutoDisabledEvent),
            nameof(InboundWebhookReceivedEvent),
            nameof(InboundWebhookRejectedEvent),
        };

    /// <summary>The catalogue, grouped by category for the dashboard subscribe checklist.</summary>
    public static IReadOnlyList<OutboundWebhookEventCatalogueEntry> Entries { get; } =
    [
        // ── Stream ──
        Entry("ChannelOnlineEvent", "stream"),
        Entry("ChannelOfflineEvent", "stream"),
        Entry("ChannelUpdatedEvent", "stream"),
        Entry("AdBreakBeganEvent", "stream"),
        // ── Followers & Subscriptions ──
        Entry("FollowEvent", "followers_subscriptions"),
        Entry("NewSubscriptionEvent", "followers_subscriptions"),
        Entry("ResubscriptionEvent", "followers_subscriptions"),
        Entry("GiftSubscriptionEvent", "followers_subscriptions"),
        Entry("SubscriptionEndedEvent", "followers_subscriptions"),
        // ── Bits & Cheers ──
        Entry("CheerEvent", "bits_cheers"),
        Entry("BitsUsedEvent", "bits_cheers"),
        // ── Raids & Shoutouts ──
        Entry("RaidEvent", "raids_shoutouts"),
        Entry("OutgoingRaidEvent", "raids_shoutouts"),
        Entry("ShoutoutReceivedEvent", "raids_shoutouts"),
        Entry("ShoutoutSentEvent", "raids_shoutouts"),
        // ── Channel Points ──
        Entry("RewardRedeemedEvent", "channel_points"),
        Entry("RewardRedemptionUpdatedEvent", "channel_points"),
        Entry("AutomaticRewardRedeemedEvent", "channel_points"),
        Entry("CustomPowerUpRedeemedEvent", "channel_points"),
        Entry("RewardCreatedEvent", "channel_points"),
        Entry("RewardUpdatedEvent", "channel_points"),
        Entry("RewardRemovedEvent", "channel_points"),
        // ── Polls & Predictions ──
        Entry("PollBeganEvent", "polls_predictions"),
        Entry("PollProgressEvent", "polls_predictions"),
        Entry("PollEndedEvent", "polls_predictions"),
        Entry("PredictionBeganEvent", "polls_predictions"),
        Entry("PredictionProgressEvent", "polls_predictions"),
        Entry("PredictionLockedEvent", "polls_predictions"),
        Entry("PredictionEndedEvent", "polls_predictions"),
        // ── Goals ──
        Entry("GoalBeganEvent", "goals"),
        Entry("GoalProgressEvent", "goals"),
        Entry("GoalEndedEvent", "goals"),
        // ── Hype Train ──
        Entry("HypeTrainBeganEvent", "hype_train"),
        Entry("HypeTrainProgressEvent", "hype_train"),
        Entry("HypeTrainEndedEvent", "hype_train"),
        // ── Charity ──
        Entry("CharityCampaignStartedEvent", "charity"),
        Entry("CharityDonationEvent", "charity"),
        Entry("CharityCampaignProgressEvent", "charity"),
        Entry("CharityCampaignStoppedEvent", "charity"),
        // ── Chat ──
        Entry("ChatMessageReceivedEvent", "chat"),
        Entry("WhisperReceivedEvent", "chat"),
        Entry("ChatNotificationEvent", "chat"),
        Entry("FirstTimeChatterDetectedEvent", "chat"),
        // ── Moderation ──
        Entry("UserBannedEvent", "moderation"),
        Entry("UserTimedOutEvent", "moderation"),
        Entry("UserUnbannedEvent", "moderation"),
        Entry("ModeratorAddedEvent", "moderation"),
        Entry("ModeratorRemovedEvent", "moderation"),
        Entry("VipAddedEvent", "moderation"),
        Entry("VipRemovedEvent", "moderation"),
        Entry("WarningSentEvent", "moderation"),
        Entry("WarningAcknowledgedEvent", "moderation"),
        Entry("ModerationActionTakenEvent", "moderation"),
        Entry("ShieldModeBeganEvent", "moderation"),
        Entry("ShieldModeEndedEvent", "moderation"),
        Entry("UnbanRequestCreatedEvent", "moderation"),
        Entry("UnbanRequestResolvedEvent", "moderation"),
        Entry("ChatClearedEvent", "moderation"),
        Entry("ChatMessageDeletedEvent", "moderation"),
        Entry("SuspiciousUserMessageEvent", "moderation"),
        Entry("SuspiciousUserUpdatedEvent", "moderation"),
        // ── Collaboration ──
        Entry("GuestStarSessionBeganEvent", "collaboration"),
        Entry("GuestStarSessionEndedEvent", "collaboration"),
        Entry("GuestStarGuestUpdatedEvent", "collaboration"),
        Entry("SharedChatBeganEvent", "collaboration"),
        Entry("SharedChatUpdatedEvent", "collaboration"),
        Entry("SharedChatEndedEvent", "collaboration"),
        // ── Supporters & Custom ──
        Entry("SupporterEventReceived", "supporters_custom"),
        Entry("CustomDataReceivedEvent", "supporters_custom"),
    ];

    /// <summary>
    /// Builds an entry with resource keys only: <c>webhook_event_{snake_type}_label</c> and
    /// <c>webhook_event_category_{category}</c>.
    /// </summary>
    private static OutboundWebhookEventCatalogueEntry Entry(string eventType, string category) =>
        new(
            eventType,
            $"webhook_event_{Regex.Replace(eventType, "(?<!^)([A-Z])", "_$1").ToLowerInvariant()}_label",
            $"webhook_event_category_{category}"
        );

    private static readonly IReadOnlySet<string> KnownTypes = new HashSet<string>(
        Entries.Select(e => e.EventType),
        StringComparer.Ordinal
    );

    /// <summary>True when the type is a curated, subscribable business event (never a lifecycle type).</summary>
    public static bool IsSubscribable(string eventType) => KnownTypes.Contains(eventType);

    /// <summary>True when the type is a §9 deny-listed webhook-lifecycle event.</summary>
    public static bool IsLifecycle(string eventType) => LifecycleDenyList.Contains(eventType);
}
