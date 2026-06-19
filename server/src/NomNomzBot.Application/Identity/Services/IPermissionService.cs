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

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// Application service for checking and managing permissions within channels.
/// </summary>
public interface IPermissionService
{
    /// <summary>Check if a user has a specific permission in a channel.</summary>
    Task<Result<bool>> CheckPermissionAsync(
        string userId,
        string broadcasterId,
        string permission,
        CancellationToken cancellationToken = default
    );

    /// <summary>Grant a permission to a user in a channel.</summary>
    Task<Result> GrantAsync(
        string broadcasterId,
        string userId,
        string permission,
        CancellationToken cancellationToken = default
    );

    /// <summary>Revoke a permission from a user in a channel.</summary>
    Task<Result> RevokeAsync(
        string broadcasterId,
        string userId,
        string permission,
        CancellationToken cancellationToken = default
    );

    /// <summary>Get all effective permissions for a user in a channel.</summary>
    Task<Result<IReadOnlyList<string>>> GetEffectivePermissionsAsync(
        string userId,
        string broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Check if a user has access to a channel at all.</summary>
    Task<bool> HasChannelAccessAsync(
        string userId,
        string broadcasterId,
        CancellationToken cancellationToken = default
    );
}
