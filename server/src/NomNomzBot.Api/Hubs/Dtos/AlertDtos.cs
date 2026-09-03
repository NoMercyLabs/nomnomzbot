// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Api.Hubs.Dtos;

/// <summary>Generic channel event wrapper sent via SignalR ChannelEvent method.</summary>
public record ChannelEventDto(
    string Type,
    string BroadcasterId,
    string? UserId,
    string? UserDisplayName,
    object? Data,
    string Timestamp
);

/// <summary>Generic alert DTO for one-off dashboard notifications.</summary>
public record AlertDto(string Type, string? Message, object? Data);

// ─── Alert-specific data DTOs (used as ChannelEventDto.Data) ─────────────────
//
// Every alert record below also exposes the CANONICAL widget-facing vocabulary as computed, get-only
// properties on top of its own event-specific fields: `User` (the display name of whoever the alert is
// about) on all of them, plus the event's headline scalar under the name the template vocabulary already
// uses — {user}, {viewers}, {amount}, {months}. The event-specific names each record was born with
// (DisplayName / GifterDisplayName / FromDisplayName / ViewerCount / Bits / Count) are unchanged, so
// existing readers keep working; the canonical names exist because the payload is a PUBLIC contract that
// third-party widget authors code against, and "which of the four subject-name spellings does THIS event
// use?" is not something they can be expected to guess. A widget that reads `data.user` — the same word
// the seeded chat templates use — now gets a real name instead of silently falling back to a placeholder.

/// <summary>
/// <paramref name="AvatarUrl"/>/<paramref name="Pronouns"/>/<paramref name="CommunityStanding"/> are additive
/// hub-broadcast-layer enrichment (<c>IHubUserEnricher</c>) — null when the viewer has no internal <c>User</c>
/// row yet, no avatar/pronouns on file, or no recorded standing in this channel.
/// </summary>
public record FollowAlertDto(
    string UserId,
    string DisplayName,
    string Login,
    DateTimeOffset? FollowedAt,
    string? AvatarUrl = null,
    string? Pronouns = null,
    string? CommunityStanding = null
)
{
    /// <summary>Canonical subject name — see the vocabulary note above.</summary>
    public string User => DisplayName;
}

public record SubscriptionAlertDto(string UserId, string DisplayName, string Tier)
{
    /// <summary>Canonical subject name — see the vocabulary note above.</summary>
    public string User => DisplayName;
}

public record ResubAlertDto(
    string UserId,
    string DisplayName,
    string Tier,
    int Months,
    int Streak,
    string? Message
)
{
    /// <summary>Canonical subject name — see the vocabulary note above.</summary>
    public string User => DisplayName;
}

public record GiftSubAlertDto(
    string? GifterId,
    string GifterDisplayName,
    string Tier,
    int Count,
    bool Anonymous
)
{
    /// <summary>Canonical subject name — the GIFTER, the only identity a gift-sub event carries.</summary>
    public string User => GifterDisplayName;

    /// <summary>Canonical headline scalar — how many subs were gifted.</summary>
    public int Amount => Count;
}

public record CheerAlertDto(
    string? UserId,
    string DisplayName,
    int Bits,
    string Message,
    bool Anonymous
)
{
    /// <summary>Canonical subject name — see the vocabulary note above.</summary>
    public string User => DisplayName;

    /// <summary>Canonical headline scalar — the bits cheered.</summary>
    public int Amount => Bits;
}

public record RaidAlertDto(
    string FromUserId,
    string FromDisplayName,
    string FromLogin,
    int ViewerCount
)
{
    /// <summary>Canonical subject name — the raiding channel.</summary>
    public string User => FromDisplayName;

    /// <summary>Canonical headline scalar — the size of the incoming raid party.</summary>
    public int Viewers => ViewerCount;
}

public record ChatClearedDto(string ClearedByUserId);

public record MessageDeletedDto(string MessageId, string DeletedByUserId, string TargetUserId);

public record IntegrationEventDto(string Integration);

// ─── Poll alert data DTOs ─────────────────────────────────────────────────────

public record PollChoiceDto(string Id, string Title, int Votes, int ChannelPointsVotes);

public record PollBeganAlertDto(
    string PollId,
    string Title,
    IReadOnlyList<PollChoiceDto> Choices,
    int DurationSeconds,
    DateTimeOffset EndsAt
);

public record PollProgressAlertDto(
    string PollId,
    string Title,
    IReadOnlyList<PollChoiceDto> Choices,
    DateTimeOffset EndsAt
);

public record PollEndedAlertDto(
    string PollId,
    string Title,
    string Status,
    IReadOnlyList<PollChoiceDto> Choices,
    string? WinningChoiceId
);

// ─── Prediction alert data DTOs ───────────────────────────────────────────────

public record PredictionOutcomeDto(
    string Id,
    string Title,
    int ChannelPoints,
    int Users,
    string Color
);

public record PredictionBeganAlertDto(
    string PredictionId,
    string Title,
    IReadOnlyList<PredictionOutcomeDto> Outcomes,
    int WindowSeconds,
    DateTimeOffset LocksAt
);

public record PredictionProgressAlertDto(
    string PredictionId,
    string Title,
    IReadOnlyList<PredictionOutcomeDto> Outcomes,
    DateTimeOffset LocksAt
);

public record PredictionLockedAlertDto(
    string PredictionId,
    string Title,
    IReadOnlyList<PredictionOutcomeDto> Outcomes
);

public record PredictionEndedAlertDto(
    string PredictionId,
    string Title,
    string Status,
    IReadOnlyList<PredictionOutcomeDto> Outcomes,
    string? WinningOutcomeId
);

// ─── Hype train alert data DTOs ───────────────────────────────────────────────

public record HypeTrainContributionDto(
    string UserId,
    string UserLogin,
    string UserDisplayName,
    string Type,
    int Total
);

public record HypeTrainBeganAlertDto(
    string HypeTrainId,
    int Level,
    int Total,
    int Progress,
    int Goal,
    IReadOnlyList<HypeTrainContributionDto> TopContributions,
    DateTimeOffset ExpiresAt
);

public record HypeTrainProgressAlertDto(
    string HypeTrainId,
    int Level,
    int Total,
    int Progress,
    int Goal,
    IReadOnlyList<HypeTrainContributionDto> TopContributions,
    DateTimeOffset ExpiresAt
);

public record HypeTrainEndedAlertDto(
    string HypeTrainId,
    int Level,
    int Total,
    IReadOnlyList<HypeTrainContributionDto> TopContributions,
    DateTimeOffset EndedAt
);

// ─── Shoutout alert data DTOs ─────────────────────────────────────────────────

public record ShoutoutSentAlertDto(string ToUserId, string ToDisplayName);

/// <summary>
/// <paramref name="AvatarUrl"/>/<paramref name="Pronouns"/>/<paramref name="CommunityStanding"/> are additive
/// hub-broadcast-layer enrichment (<c>IHubUserEnricher</c>), keyed off the shouting-out broadcaster's Twitch id
/// — usually null since that broadcaster is rarely also a recorded viewer of this channel.
/// </summary>
public record ShoutoutReceivedAlertDto(
    string FromBroadcasterId,
    string FromBroadcasterDisplayName,
    string FromBroadcasterLogin,
    int ViewerCount,
    string? AvatarUrl = null,
    string? Pronouns = null,
    string? CommunityStanding = null
);

// ─── Ad break alert data DTO ──────────────────────────────────────────────────

public record AdBreakBeganAlertDto(
    int DurationSeconds,
    bool IsAutomatic,
    DateTimeOffset StartedAt,
    string? RequesterUserId,
    string? RequesterDisplayName
);

// ─── Shield mode alert data DTOs ──────────────────────────────────────────────

public record ShieldModeBeganAlertDto(
    string ModeratorId,
    string ModeratorDisplayName,
    DateTimeOffset StartedAt
);

public record ShieldModeEndedAlertDto(
    string ModeratorId,
    string ModeratorDisplayName,
    DateTimeOffset EndedAt
);

// ─── Moderator / VIP role change alert DTO ────────────────────────────────────

/// <summary>
/// Shared shape for moderator and VIP role grants/revocations (identical fields on all four events).
/// <paramref name="AvatarUrl"/>/<paramref name="Pronouns"/>/<paramref name="CommunityStanding"/> are additive
/// hub-broadcast-layer enrichment (<c>IHubUserEnricher</c>).
/// </summary>
public record RoleChangedAlertDto(
    string UserId,
    string UserDisplayName,
    string UserLogin,
    string? AvatarUrl = null,
    string? Pronouns = null,
    string? CommunityStanding = null
);

// ─── Outbound webhook delivery alert DTOs (S099-ATTEMPTED-EVENTS-CONSUMED) ────

/// <summary>A failed or dead-lettered outbound webhook delivery attempt — the states an operator needs to notice.</summary>
public record WebhookDeliveryAttemptFailedAlertDto(
    string OutboundEndpointId,
    string WebhookMessageId,
    int Attempt,
    string Status,
    int? ResponseCode,
    DateTime? NextRetryAt
);

/// <summary>An outbound webhook endpoint was auto-disabled after too many consecutive delivery failures.</summary>
public record WebhookEndpointAutoDisabledAlertDto(
    string OutboundEndpointId,
    int ConsecutiveFailureCount,
    string Reason
);

// ─── AutoMod review queue alert DTOs (S-OWN22) ────────────────────────────────

/// <summary>The AutoMod review queue changed — a message was held, or a held message's review was resolved.</summary>
public record AutoModQueueChangedAlertDto(string MessageId, string UserDisplayName, string Change);
