// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// Application service for user management.
/// </summary>
public interface IUserService
{
    /// <summary>Get the currently authenticated user from the request context.</summary>
    Task<Result<CurrentUserDto>> GetCurrentUserAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get or create a user by their platform ID. Used when a user is first seen in chat.
    /// <paramref name="provider"/> names the platform the id belongs to (<see cref="AuthEnums.Platform"/>
    /// key) so a YouTube chatter resolves under a <c>youtube</c> identity, never a fake Twitch one.
    /// </summary>
    Task<Result<UserDto>> GetOrCreateAsync(
        string platformUserId,
        string username,
        string displayName,
        string provider = AuthEnums.Platform.Twitch,
        CancellationToken cancellationToken = default
    );

    /// <summary>Update a user's profile information.</summary>
    Task<Result<UserProfileDto>> UpdateProfileAsync(
        string userId,
        UpdateUserProfileRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Search users by name or display name.</summary>
    Task<Result<PagedList<UserSearchResult>>> SearchAsync(
        string query,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    );

    /// <summary>Get a user by their ID.</summary>
    Task<Result<UserDto>> GetAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Get a user's full profile.</summary>
    Task<Result<UserProfileDto>> GetProfileAsync(
        string userId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Get a GDPR data summary for a user.</summary>
    Task<Result<UserStatsDto>> GetStatsAsync(
        string userId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Get the list of channels a user appears in (GDPR data view).</summary>
    Task<Result<List<UserChannelAppearanceDto>>> GetUserChannelsAsync(
        string userId,
        CancellationToken cancellationToken = default
    );

    /// <summary>List all users for the admin panel.</summary>
    Task<Result<PagedList<AdminUserDto>>> ListAdminUsersAsync(
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    );
}
