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

public record FollowAlertDto(
    string UserId,
    string DisplayName,
    string Login,
    DateTimeOffset? FollowedAt
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
