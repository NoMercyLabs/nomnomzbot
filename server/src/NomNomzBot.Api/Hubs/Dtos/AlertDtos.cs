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
);

public record SubscriptionAlertDto(string UserId, string DisplayName, string Tier);

public record ResubAlertDto(
    string UserId,
    string DisplayName,
    string Tier,
    int Months,
    int Streak,
    string? Message
);

public record GiftSubAlertDto(
    string? GifterId,
    string GifterDisplayName,
    string Tier,
    int Count,
    bool Anonymous
);

public record CheerAlertDto(
    string? UserId,
    string DisplayName,
    int Bits,
    string Message,
    bool Anonymous
);

public record RaidAlertDto(
    string FromUserId,
    string FromDisplayName,
    string FromLogin,
    int ViewerCount
);

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
