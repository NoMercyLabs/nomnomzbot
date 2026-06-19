// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Identity.Dtos;

/// <summary>Full channel detail for dashboard views.</summary>
public sealed record ChannelDto(
    string Id,
    string Name,
    string DisplayName,
    string? ProfileImageUrl,
    bool IsLive,
    bool IsOnboarded,
    string? Title,
    string? GameName,
    int? ViewerCount,
    DateTime? BotJoinedAt,
    string SubscriptionTier,
    string? Language,
    DateTime CreatedAt
);

/// <summary>Lightweight channel info for lists and dropdowns.</summary>
public sealed record ChannelSummaryDto(
    string Id,
    string Login,
    string DisplayName,
    string? ProfileImageUrl,
    bool IsLive,
    string Role,
    int? ViewerCount,
    string? OverlayToken
);

/// <summary>Lightweight channel info returned when looking up by overlay token.</summary>
public sealed record ChannelOverlayInfo(string BroadcasterId, string DisplayName);

/// <summary>Request to update channel settings.</summary>
public sealed record UpdateChannelSettingsDto
{
    public string? DisplayName { get; init; }
    public string? SubscriptionTier { get; init; }
    public string? Prefix { get; init; }
    public string? Locale { get; init; }
    public bool? AutoJoin { get; init; }
}

/// <summary>Request to create/onboard a new channel.</summary>
public sealed record CreateChannelRequest
{
    public required string BroadcasterId { get; init; }
    public string? DisplayName { get; init; }
}
