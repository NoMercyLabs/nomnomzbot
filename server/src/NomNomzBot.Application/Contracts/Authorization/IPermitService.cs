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
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Application.Contracts.Authorization;

/// <summary>
/// <c>!permit</c> / <c>!unpermit</c> — temporary per-user delegation (roles-permissions §3.6). A role grant
/// lifts a user to a management role; a capability grant hands one named user a single action key (§0.2 — a
/// dangerous capability is delegated to an individual, never raised on a role tier). Replaces generic grant.
/// </summary>
public interface IPermitService
{
    /// <summary>
    /// <c>!permit @user &lt;role&gt;</c>. No-escalation: the granted role must be ≤ the grantor's resolved level
    /// (else <c>FORBIDDEN</c>). Optional expiry. Emits <c>PermitGrantedEvent</c>.
    /// </summary>
    Task<Result<PermitGrantDto>> GrantRoleAsync(
        Guid broadcasterId,
        Guid targetUserId,
        ManagementRole role,
        Guid grantedByUserId,
        DateTime? expiresAt,
        string? reason,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// <c>!permit @user &lt;actionKey&gt;</c> — a per-user capability grant. Guardrails: default-deny (the action's
    /// <c>IsGrantableViaPermit</c> must be true), and no-escalation (the grantor must themselves be authorized
    /// for the action — so a Critical action is grantable only by someone who clears its floor). Optional
    /// expiry. Emits <c>PermitGrantedEvent</c>.
    /// </summary>
    Task<Result<PermitGrantDto>> GrantCapabilityAsync(
        Guid broadcasterId,
        Guid targetUserId,
        string actionKey,
        Guid grantedByUserId,
        DateTime? expiresAt,
        string? reason,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// <c>!unpermit @user [actionKey|role]</c>. Revokes matching active grants (sets <c>RevokedAt</c>); a null
    /// selector revokes all of the user's active grants. Emits <c>PermitRevokedEvent</c> per revoked grant.
    /// </summary>
    Task<Result> RevokeAsync(
        Guid broadcasterId,
        Guid targetUserId,
        string? actionKeyOrRole,
        Guid revokedByUserId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Active (non-expired, non-revoked) grants for a channel — permissions UI + audit.</summary>
    Task<Result<IReadOnlyList<PermitGrantDto>>> ListActiveGrantsAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Sweep (called by a background service across all channels): revokes grants past their expiry with
    /// reason <c>"expired"</c>, emitting <c>PermitRevokedEvent</c> per row. Returns the number revoked.
    /// </summary>
    Task<Result<int>> ExpireDueGrantsAsync(CancellationToken cancellationToken = default);
}
