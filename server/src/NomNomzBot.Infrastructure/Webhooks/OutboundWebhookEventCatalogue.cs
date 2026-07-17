// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

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
        new("ChannelOnlineEvent", "Stream went live", "Stream"),
        new("ChannelOfflineEvent", "Stream went offline", "Stream"),
        new("ChannelUpdatedEvent", "Channel title or category changed", "Stream"),
        new("AdBreakBeganEvent", "Ad break started", "Stream"),
        // ── Followers & Subscriptions ──
        new("FollowEvent", "New follower", "Followers & Subscriptions"),
        new("NewSubscriptionEvent", "New subscription", "Followers & Subscriptions"),
        new("ResubscriptionEvent", "Resubscription", "Followers & Subscriptions"),
        new("GiftSubscriptionEvent", "Gifted subscription", "Followers & Subscriptions"),
        new("SubscriptionEndedEvent", "Subscription ended", "Followers & Subscriptions"),
        // ── Bits & Cheers ──
        new("CheerEvent", "Cheer", "Bits & Cheers"),
        new("BitsUsedEvent", "Bits used", "Bits & Cheers"),
        // ── Raids & Shoutouts ──
        new("RaidEvent", "Incoming raid", "Raids & Shoutouts"),
        new("ShoutoutReceivedEvent", "Shoutout received", "Raids & Shoutouts"),
        new("ShoutoutSentEvent", "Shoutout sent", "Raids & Shoutouts"),
        // ── Channel Points ──
        new("RewardRedeemedEvent", "Channel-point reward redeemed", "Channel Points"),
        new(
            "RewardRedemptionUpdatedEvent",
            "Reward redemption approved or rejected",
            "Channel Points"
        ),
        new("AutomaticRewardRedeemedEvent", "Automatic reward redeemed", "Channel Points"),
        new("CustomPowerUpRedeemedEvent", "Power-up redeemed", "Channel Points"),
        new("RewardCreatedEvent", "Reward created", "Channel Points"),
        new("RewardUpdatedEvent", "Reward updated", "Channel Points"),
        new("RewardRemovedEvent", "Reward removed", "Channel Points"),
        // ── Polls & Predictions ──
        new("PollBeganEvent", "Poll started", "Polls & Predictions"),
        new("PollProgressEvent", "Poll progress", "Polls & Predictions"),
        new("PollEndedEvent", "Poll ended", "Polls & Predictions"),
        new("PredictionBeganEvent", "Prediction started", "Polls & Predictions"),
        new("PredictionProgressEvent", "Prediction progress", "Polls & Predictions"),
        new("PredictionLockedEvent", "Prediction locked", "Polls & Predictions"),
        new("PredictionEndedEvent", "Prediction ended", "Polls & Predictions"),
        // ── Goals ──
        new("GoalBeganEvent", "Goal started", "Goals"),
        new("GoalProgressEvent", "Goal progress", "Goals"),
        new("GoalEndedEvent", "Goal ended", "Goals"),
        // ── Hype Train ──
        new("HypeTrainBeganEvent", "Hype train started", "Hype Train"),
        new("HypeTrainProgressEvent", "Hype train progress", "Hype Train"),
        new("HypeTrainEndedEvent", "Hype train ended", "Hype Train"),
        // ── Charity ──
        new("CharityCampaignStartedEvent", "Charity campaign started", "Charity"),
        new("CharityDonationEvent", "Charity donation", "Charity"),
        new("CharityCampaignProgressEvent", "Charity campaign progress", "Charity"),
        new("CharityCampaignStoppedEvent", "Charity campaign stopped", "Charity"),
        // ── Chat ──
        new("ChatMessageReceivedEvent", "Chat message", "Chat"),
        new("WhisperReceivedEvent", "Whisper received", "Chat"),
        new("ChatNotificationEvent", "Chat notification", "Chat"),
        new("FirstTimeChatterDetectedEvent", "First-time chatter", "Chat"),
        // ── Moderation ──
        new("UserBannedEvent", "User banned", "Moderation"),
        new("UserTimedOutEvent", "User timed out", "Moderation"),
        new("UserUnbannedEvent", "User unbanned", "Moderation"),
        new("ModeratorAddedEvent", "Moderator added", "Moderation"),
        new("ModeratorRemovedEvent", "Moderator removed", "Moderation"),
        new("VipAddedEvent", "VIP added", "Moderation"),
        new("VipRemovedEvent", "VIP removed", "Moderation"),
        new("WarningSentEvent", "Warning sent", "Moderation"),
        new("WarningAcknowledgedEvent", "Warning acknowledged", "Moderation"),
        new("ModerationActionTakenEvent", "Moderation action taken", "Moderation"),
        new("ShieldModeBeganEvent", "Shield mode enabled", "Moderation"),
        new("ShieldModeEndedEvent", "Shield mode disabled", "Moderation"),
        new("UnbanRequestCreatedEvent", "Unban request created", "Moderation"),
        new("UnbanRequestResolvedEvent", "Unban request resolved", "Moderation"),
        new("ChatClearedEvent", "Chat cleared", "Moderation"),
        new("ChatMessageDeletedEvent", "Chat message deleted", "Moderation"),
        new("SuspiciousUserMessageEvent", "Suspicious user message", "Moderation"),
        new("SuspiciousUserUpdatedEvent", "Suspicious user updated", "Moderation"),
        // ── Collaboration ──
        new("GuestStarSessionBeganEvent", "Guest Star session started", "Collaboration"),
        new("GuestStarSessionEndedEvent", "Guest Star session ended", "Collaboration"),
        new("GuestStarGuestUpdatedEvent", "Guest Star guest updated", "Collaboration"),
        new("SharedChatBeganEvent", "Shared chat started", "Collaboration"),
        new("SharedChatUpdatedEvent", "Shared chat updated", "Collaboration"),
        new("SharedChatEndedEvent", "Shared chat ended", "Collaboration"),
        // ── Supporters & Custom ──
        new(
            "SupporterEventReceived",
            "Supporter event (Ko-fi, StreamElements, …)",
            "Supporters & Custom"
        ),
        new("CustomDataReceivedEvent", "Custom inbound event", "Supporters & Custom"),
    ];

    private static readonly IReadOnlySet<string> KnownTypes = new HashSet<string>(
        Entries.Select(e => e.EventType),
        StringComparer.Ordinal
    );

    /// <summary>True when the type is a curated, subscribable business event (never a lifecycle type).</summary>
    public static bool IsSubscribable(string eventType) => KnownTypes.Contains(eventType);

    /// <summary>True when the type is a §9 deny-listed webhook-lifecycle event.</summary>
    public static bool IsLifecycle(string eventType) => LifecycleDenyList.Contains(eventType);
}
