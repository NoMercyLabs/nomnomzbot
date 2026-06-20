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

namespace NomNomzBot.Application.Contracts.Authorization;

/// <summary>
/// Resolves a caller's effective authorization for a channel (roles-permissions §3.2). The effective level is
/// the <c>MAX</c> across planes — community standing, channel-management role, and any active (non-revoked,
/// non-expired) <c>!permit</c> role grant — on the single numeric ladder. Pure read; no writes.
/// </summary>
public interface IRoleResolver
{
    /// <summary>The caller's effective unified-ladder level for the channel (MAX across planes).</summary>
    Task<Result<int>> ResolveEffectiveLevelAsync(
        Guid userId,
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>The full per-plane breakdown + winning source, for the permissions UI / debugging.</summary>
    Task<Result<ResolvedAccessDto>> ResolveAccessAsync(
        Guid userId,
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Whether the caller holds the given action key — via a direct capability grant, or because their
    /// resolved level meets the action's effective required level (override clamped to floor). Unknown action
    /// keys fail closed (false).
    /// </summary>
    Task<Result<bool>> HasCapabilityAsync(
        Guid userId,
        Guid broadcasterId,
        string actionKey,
        CancellationToken cancellationToken = default
    );
}
