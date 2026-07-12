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

/// <summary>
/// Lightweight channel info for lists and dropdowns. <c>ChatColor</c> is the streamer's Twitch chat color
/// (#RRGGBB), populated from <c>User.Color</c> when known — null until the first login after the color-sync
/// feature is deployed, or when the user has no color set; the dashboard uses it as the dynamic accent token
/// (design-system §2).
/// </summary>
public sealed record ChannelSummaryDto(
    string Id,
    string Login,
    string DisplayName,
    string? ProfileImageUrl,
    bool IsLive,
    string Role,
    int? ViewerCount,
    string? OverlayToken,
    string? ChatColor = null
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

/// <summary>
/// A channel's built-in-command personality tone. <see cref="Personality"/> is the canonical tone token
/// (<c>PersonalityTone.*</c>); <see cref="Available"/> lists every selectable tone so a picker needs no
/// second call.
/// </summary>
public sealed record ChannelPersonalityDto(string Personality, IReadOnlyList<string> Available);

/// <summary>Request to set a channel's built-in-command personality tone.</summary>
public sealed record SetChannelPersonalityRequest
{
    /// <summary>One of <c>PersonalityTone.All</c> (case-insensitive).</summary>
    public required string Personality { get; init; }
}
