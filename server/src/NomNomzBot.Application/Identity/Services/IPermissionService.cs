// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
