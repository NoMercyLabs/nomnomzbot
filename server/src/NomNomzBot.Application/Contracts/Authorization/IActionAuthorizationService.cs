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
/// Gate 2 — per-action authorization + per-action config (roles-permissions §3.3). Reads the caller's
/// resolved level (<see cref="IRoleResolver"/>) and compares it to the action's effective required level
/// (<c>clamp(override ?? default, floor, Broadcaster)</c>). Replaces the generic permission-string authz.
/// </summary>
public interface IActionAuthorizationService
{
    /// <summary>
    /// Allows iff the caller's resolved level meets the action's effective required level. Emits an
    /// <c>AuthorizationDeniedEvent</c> on a level deny; fails closed (denied) on an unknown action key.
    /// </summary>
    Task<Result<bool>> AuthorizeActionAsync(
        Guid userId,
        Guid broadcasterId,
        string actionKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>The resolved required level for one action in a channel (override clamped to floor). Read-only.</summary>
    Task<Result<int>> GetEffectiveLevelAsync(
        Guid broadcasterId,
        string actionKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>The full per-channel action matrix (definition + default/floor/tier + override + effective).</summary>
    Task<Result<IReadOnlyList<ActionPermissionDto>>> GetActionMatrixAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Upserts a channel override of an action's required level. Validates the clamp to <c>[floor, Broadcaster]</c>
    /// and rejects a level below the action's floor (<c>VALIDATION_FAILED</c>). Emits
    /// <c>ActionLevelOverriddenEvent</c>. Returns the stored effective level.
    /// </summary>
    Task<Result<int>> SetActionOverrideAsync(
        Guid broadcasterId,
        string actionKey,
        int level,
        Guid setByUserId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Soft-deletes the override → the action reverts to its global default. Emits the override event.</summary>
    Task<Result> ResetActionOverrideAsync(
        Guid broadcasterId,
        string actionKey,
        Guid setByUserId,
        CancellationToken cancellationToken = default
    );
}
