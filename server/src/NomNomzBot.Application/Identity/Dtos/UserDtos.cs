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

/// <summary>The currently authenticated user, as surfaced to the dashboard.</summary>
public sealed record CurrentUserDto(
    string Id,
    string Username,
    string DisplayName,
    string? ProfileImageUrl,
    string? Color,
    string BroadcasterType,
    bool IsAdmin,
    DateTime CreatedAt
);

/// <summary>Full user information.</summary>
public sealed record UserDto(
    string Id,
    string Username,
    string DisplayName,
    string? ProfileImageUrl,
    string? Email,
    DateTime CreatedAt,
    DateTime LastLoginAt
);

/// <summary>User profile information for display.</summary>
public sealed record UserProfileDto(
    string Id,
    string Username,
    string DisplayName,
    string? ProfileImageUrl,
    string? Email,
    string? Pronoun,
    DateTime CreatedAt,
    DateTime LastLoginAt
);

/// <summary>Lightweight user info for search results and dropdowns.</summary>
public sealed record UserSearchResult(
    string Id,
    string Username,
    string DisplayName,
    string? ProfileImageUrl
);

/// <summary>Request to update a user profile.</summary>
public sealed record UpdateUserProfileRequest
{
    public string? DisplayName { get; init; }
    public string? Email { get; init; }

    /// <summary>The <see cref="Pronoun.Id"/> to assign, or <c>null</c> to leave unchanged, or <c>0</c> to clear.</summary>
    public int? PronounId { get; init; }
}

/// <summary>GDPR data summary for a user.</summary>
public sealed record UserStatsDto(
    int MessageCount,
    double WatchHours,
    int ChannelsCount,
    int CommandsUsed,
    DateTime? FirstSeen,
    DateTime? LastActive,
    bool ExportAvailable
);

/// <summary>Channel appearance record for GDPR user data view.</summary>
public sealed record UserChannelAppearanceDto(
    string Id,
    string ChannelId,
    string ChannelName,
    string FollowDate,
    int Messages,
    string WatchTime
);
