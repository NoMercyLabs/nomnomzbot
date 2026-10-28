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
    /// Whether a platform-wide network block (active or partial, never lifted) stands against the user. It is
    /// not tenant-scoped: a blocked actor is denied every Gate-2 action in every channel, whatever their level
    /// there.
    /// </summary>
    Task<Result<bool>> IsNetworkBlockedAsync(
        Guid userId,
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
